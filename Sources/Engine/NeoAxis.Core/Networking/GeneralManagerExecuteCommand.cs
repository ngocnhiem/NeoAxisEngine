// Copyright (C) NeoAxis Group Ltd. 8 Copthall, Roseau Valley, 00152 Commonwealth of Dominica.
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Net;
using System.Threading;
using NeoAxis.Editor;
using System.Threading.Tasks;
using System.Net.Http;

namespace NeoAxis.Networking
{
	public class GeneralManagerExecuteCommand
	{
		static HttpClient httpClient;

		public string FunctionName = "";
		public bool RequireUserLogin;
		public string ServerCheckCode;
		public List<ParameterItem> Parameters = new List<ParameterItem>();
		public RequestMethodEnum RequestMethod;
		public byte[] ContentData;
		public object Tag;
		public int? Timeout;

		public volatile ResultClass Result;

		ThreadItem currentThread;

		///////////////////////////////////////////////

		public class ParameterItem
		{
			public string Name;
			public string Value;
			public bool EncodeBase64Url;
		}

		///////////////////////////////////////////////

		public enum RequestMethodEnum
		{
			Get,
			Post,
		}

		///////////////////////////////////////////////

		public delegate void ProcessedDelegate( GeneralManagerExecuteCommand command );
		/// <summary>
		/// Called from thread.
		/// </summary>
		public event ProcessedDelegate Processed;

		///////////////////////////////////////////////

		class ThreadItem
		{
			public Thread thread;
			public bool callProcessedEventFromMainThread;
			public bool needStop;
			//public string search;
			//public StoreManager.FilterSettingsClass filterSettings;
		}

		///////////////////////////////////////////////

		public class ResultClass
		{
			public TextBlock Data;
			//public string Data = "";
			public string Error = "";
			public DateTime TimeCreated;
		}

		///////////////////////////////////////////////

		public class ResultDownloadFileClass
		{
			public string Error = "";
			//public DateTime TimeCreated;
		}

		///////////////////////////////////////////////

		void ThreadFunction( object threadItem2 )
		{
			ThreadItem threadItem = (ThreadItem)threadItem2;

			try
			{
				var url = string.Format( @"{0}/{1}/", GeneralManagerFunctions.GetHttpURL(), FunctionName );

				var paramsAdded = false;

				if( RequireUserLogin )
				{
					if( !string.IsNullOrEmpty( CloudClientProcessUtility.LoginForSecureMode ) )
					{
						//for secure mode use login and verification code from command line
						var email64 = StringUtility.EncodeToBase64URL( CloudClientProcessUtility.LoginForSecureMode );
						var hash64 = StringUtility.EncodeToBase64URL( CloudClientProcessUtility.VerificationCodeForSecureMode );
						url += $"?user={email64}&hash_code_for_secure_mode={hash64}";
					}
					else
					{
						if( !LoginUtility.GetCurrentLicense( out string email, out string hash ) )
							throw new Exception( "Please login to process." );

						var email64 = StringUtility.EncodeToBase64URL( email );
						var hash64 = StringUtility.EncodeToBase64URL( hash );
						url += $"?user={email64}&hash={hash64}";
					}

					paramsAdded = true;
				}

				//if( !string.IsNullOrEmpty( ServerCheckCode ) )
				//{
				//	url += paramsAdded ? "&" : "?";
				//	url += $"server_check_code={ServerCheckCode}";
				//	paramsAdded = true;
				//}

				foreach( var param in Parameters )
				{
					if( param.EncodeBase64Url )
					{
						var param64 = StringUtility.EncodeToBase64URL( param.Value );
						url += paramsAdded ? "&" : "?";
						url += $"{param.Name}={param64}";
					}
					else
					{
						url += paramsAdded ? "&" : "?";
						url += $"{param.Name}={param.Value}";
					}
					paramsAdded = true;
				}

				var request = (HttpWebRequest)WebRequest.Create( url );
				if( Timeout != null )
					request.Timeout = Timeout.Value;
				else
					request.Timeout = NetworkCommonSettings.GeneralManagerExecuteCommandTimeout;

				if( !string.IsNullOrEmpty( ServerCheckCode ) )
					request.Headers[ "Authorization" ] = "Bearer " + ServerCheckCode;

				if( RequestMethod == RequestMethodEnum.Post )
				{
					var contentData = ContentData ?? new byte[ 0 ];
					request.Method = "POST";
					request.ContentLength = contentData.Length;
					request.ContentType = "application/x-www-form-urlencoded";
					var dataStream = request.GetRequestStream();
					dataStream.Write( contentData, 0, contentData.Length );
					dataStream.Close();
				}

				string blockString = "";

				//!!!!can freeze?

				using( var response = (HttpWebResponse)request.GetResponse() )
				using( var stream = response.GetResponseStream() )
				using( var reader = new StreamReader( stream ) )
					blockString = reader.ReadToEnd();

				if( threadItem.needStop || EditorAPI.ClosingApplication )
					return;

				var block = TextBlock.Parse( blockString, out var error );
				if( !string.IsNullOrEmpty( error ) )
					throw new Exception( "Error of parsing the response data. " + error );

				if( threadItem.needStop || EditorAPI.ClosingApplication )
					return;

				var result = new ResultClass();

				var errorInResultData = block.GetAttribute( "Error" );
				if( !string.IsNullOrEmpty( errorInResultData ) )
					result.Error = errorInResultData;
				else
					result.Data = block;
				result.TimeCreated = DateTime.Now;

				Result = result;

				if( threadItem.callProcessedEventFromMainThread )
				{
					EngineThreading.ExecuteFromMainThreadLater( delegate ()
					{
						Processed?.Invoke( this );
					} );
				}
				else
					Processed?.Invoke( this );
			}
			catch( Exception e )
			{
				if( threadItem.needStop || EditorAPI.ClosingApplication )
					return;

				var result = new ResultClass();
				result.Error = e.Message;
				result.TimeCreated = DateTime.Now;

				Result = result;

				if( threadItem.callProcessedEventFromMainThread )
				{
					EngineThreading.ExecuteFromMainThreadLater( delegate ()
					{
						Processed?.Invoke( this );
					} );
				}
				else
					Processed?.Invoke( this );
			}
		}

		public void BeginExecution( bool callProcessedEventFromMainThread )
		{
			StopExecution();

			var thread = new Thread( ThreadFunction );
			thread.IsBackground = true;
			var threadItem = new ThreadItem() { thread = thread, callProcessedEventFromMainThread = callProcessedEventFromMainThread };
			currentThread = threadItem;

			thread.Start( threadItem );
		}

		public void StopExecution()
		{
			var item = currentThread;
			if( item != null )
				item.needStop = true;
			currentThread = null;
		}

		async Task<string> SendRequestAsync( string url, RequestMethodEnum requestMethod, byte[] contentData, string login, string password, string serverCheckCode, CancellationToken cancellationToken = default )
		{
			if( httpClient == null )
			{
				httpClient = new HttpClient();
				httpClient.Timeout = TimeSpan.FromMilliseconds( NetworkCommonSettings.GeneralManagerExecuteCommandTimeout );
			}

			var useNewClient = Timeout != null;
			var client = useNewClient ? new HttpClient() : httpClient;
			try
			{
				if( useNewClient )
				{
					if( Timeout != null )
						client.Timeout = TimeSpan.FromMilliseconds( Timeout.Value );
				}

				//!!!!not implemented
				//if( !string.IsNullOrEmpty( login ) )
				//{
				//	// Create the Basic Authentication header
				//	var byteArray = Encoding.UTF8.GetBytes( $"{login}:{password}" );
				//	var authHeader = Convert.ToBase64String( byteArray );
				//	httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue( "Basic", authHeader );
				//}


				var request = new HttpRequestMessage( requestMethod == RequestMethodEnum.Post ? HttpMethod.Post : HttpMethod.Get, url );
				if( !string.IsNullOrEmpty( serverCheckCode ) )
					request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue( "Bearer", serverCheckCode );
				if( requestMethod == RequestMethodEnum.Post )
				{
					request.Content = new ByteArrayContent( contentData ?? new byte[ 0 ] );
					request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue( "application/x-www-form-urlencoded" );
				}

				var response = await client.SendAsync( request, cancellationToken );

				response.EnsureSuccessStatusCode();

				string blockString = await response.Content.ReadAsStringAsync( cancellationToken );
				return blockString;



				//HttpResponseMessage response;
				//if( requestMethod == RequestMethodEnum.Post )
				//{
				//	var content = new ByteArrayContent( contentData ?? new byte[ 0 ] );
				//	content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue( "application/x-www-form-urlencoded" );
				//	response = await client.PostAsync( url, content, cancellationToken );
				//}
				//else
				//	response = await client.GetAsync( url, cancellationToken );

				//response.EnsureSuccessStatusCode();

				//string blockString = await response.Content.ReadAsStringAsync( cancellationToken );
				//return blockString;
			}
			finally
			{
				if( useNewClient )
					client.Dispose();
			}


			//var request = (HttpWebRequest)WebRequest.Create( url );

			//if( Timeout != null )
			//	request.Timeout = Timeout.Value;
			//else
			//	request.Timeout = NetworkCommonSettings.GeneralManagerExecuteCommandTimeout;

			//if( RequestMethod == RequestMethodEnum.Post )
			//{
			//	request.Method = "POST";
			//	request.ContentLength = contentData.Length;
			//	request.ContentType = "application/x-www-form-urlencoded";

			//	//!!!!пишет тут сразу весь контент в стрим

			//	var dataStream = request.GetRequestStream();
			//	dataStream.Write( contentData, 0, contentData.Length );
			//	dataStream.Close();
			//}

			//string blockString = "";

			//using( var response = await request.GetResponseAsync() )
			//{
			//	using( var stream = response.GetResponseStream() )
			//	using( var reader = new StreamReader( stream ) )
			//		blockString = await reader.ReadToEndAsync();
			//}


			//if( !string.IsNullOrEmpty( login ) )
			//{
			//	// Create the Basic Authentication header
			//	var byteArray = Encoding.UTF8.GetBytes( $"{login}:{password}" );
			//	var authHeader = Convert.ToBase64String( byteArray );
			//	httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue( "Basic", authHeader );
			//}

			//HttpResponseMessage response;

			//if( requestMethod == RequestMethodEnum.Post )
			//{
			//	var content = new ByteArrayContent( contentData ?? new byte[ 0 ] );
			//	content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue( "application/x-www-form-urlencoded" );

			//	response = await httpClient.PostAsync( url, content, cancellationToken );
			//}
			//else
			//	response = await httpClient.GetAsync( url, cancellationToken );

			//response.EnsureSuccessStatusCode();

			//string blockString = await response.Content.ReadAsStringAsync( cancellationToken );
			//return blockString;
		}

		public void AddParameter( string name, string value, bool encodeBase64Url )
		{
			var param = new ParameterItem();
			param.Name = name;
			param.Value = value;
			param.EncodeBase64Url = encodeBase64Url;
			Parameters.Add( param );
		}

		public async Task<ResultClass> ExecuteAsync( CancellationToken cancellationToken = default )
		{
			try
			{
				var url = string.Format( @"{0}/{1}/", GeneralManagerFunctions.GetHttpURL(), FunctionName );

				var paramsAdded = false;

				//!!!!impl
				var login = "";
				var password = "";

				if( RequireUserLogin )
				{
					if( !string.IsNullOrEmpty( CloudClientProcessUtility.LoginForSecureMode ) )
					{
						//for secure mode use login and verification code from command line
						var email64 = StringUtility.EncodeToBase64URL( CloudClientProcessUtility.LoginForSecureMode );
						var projectID64 = StringUtility.EncodeToBase64URL( CloudClientProcessUtility.ProjectID.ToString() );
						var hash64 = StringUtility.EncodeToBase64URL( CloudClientProcessUtility.VerificationCodeForSecureMode );
						url += $"?user={email64}&project_for_secure_mode={projectID64}&hash_code_for_secure_mode={hash64}";
					}
					else
					{
						if( !LoginUtility.GetCurrentLicense( out string email, out string hash ) )
							throw new Exception( "Please login to process." );

						var email64 = StringUtility.EncodeToBase64URL( email );
						var hash64 = StringUtility.EncodeToBase64URL( hash );
						url += $"?user={email64}&hash={hash64}";
					}

					paramsAdded = true;
				}

				//if( !string.IsNullOrEmpty( ServerCheckCode ) )
				//{
				//	url += paramsAdded ? "&" : "?";
				//	url += $"server_check_code={ServerCheckCode}";
				//	paramsAdded = true;
				//}

				foreach( var param in Parameters )
				{
					if( param.EncodeBase64Url )
					{
						var param64 = StringUtility.EncodeToBase64URL( param.Value );
						url += paramsAdded ? "&" : "?";
						url += $"{param.Name}={param64}";
					}
					else
					{
						url += paramsAdded ? "&" : "?";
						url += $"{param.Name}={param.Value}";
					}
					paramsAdded = true;
				}

				var blockString = await SendRequestAsync( url, RequestMethod, ContentData, login, password, ServerCheckCode, cancellationToken );

				if( EditorAPI.ClosingApplication )
					throw new Exception( "Closing application." );

				var block = TextBlock.Parse( blockString, out var error );
				if( !string.IsNullOrEmpty( error ) )
					throw new Exception( "Error of parsing the response data. " + error );

				if( EditorAPI.ClosingApplication )
					throw new Exception( "Closing application." );

				var result = new ResultClass();

				var errorInResultData = block.GetAttribute( "Error" );
				if( !string.IsNullOrEmpty( errorInResultData ) )
					result.Error = errorInResultData;
				else
					result.Data = block;
				result.TimeCreated = DateTime.Now;

				return result;
			}
			catch( Exception e )
			{
				var result = new ResultClass();
				result.Error = e.Message;
				result.TimeCreated = DateTime.Now;
				return result;
			}
		}
	}
}