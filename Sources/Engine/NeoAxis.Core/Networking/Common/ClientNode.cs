// Copyright (C) NeoAxis Group Ltd. 8 Copthall, Roseau Valley, 00152 Commonwealth of Dominica.
#if !LIDGREN
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NeoAxis.Networking
{
	public abstract class ClientNode
	{
		readonly static bool trace = false;
		internal const int maxServiceIdentifier = 255;

		static List<ClientNode> instances = new List<ClientNode>();

		bool disposed;

		//profiler
		ProfilerDataClass profilerData;

		string clientVersion = "";
		string loginData = "";
		double keepAliveInSeconds;
		int sendMessageMaxSize = 10 * 1024 * 1024;
		int receiveMessageMaxSize = 11 * 1024 * 1024;

		//connection state
		NetworkStatus lastStatus;
		bool beginConnecting;
		//bool failedToConnect;
		////ESet<string> serverServices = new ESet<string>();

		string disconnectionReason = "";
		double lastReceivedMessageTime;

		//services
		List<ClientService> services = new List<ClientService>();
		ClientService[] servicesByIdentifier = new ClientService[ maxServiceIdentifier + 1 ];
		ReadOnlyCollection<ClientService> servicesReadOnly;

		//client data
		string clientConnectHost = string.Empty;
		int clientConnectPort;
		string clientConnectAddress = string.Empty;
		internal ClientWebSocket client;

		//thread
		Thread thread;
		volatile bool threadNeedExit;
		AutoResetEvent threadNeedUpdate = new AutoResetEvent( false );
		volatile bool threadNeedExitSendClose;

		//limits. it is solved by timeout
		ConcurrentQueue<ReceivedMessage> receivedMessages = new ConcurrentQueue<ReceivedMessage>();
		ConcurrentQueue<ToProcessMessage> toProcessMessages = new ConcurrentQueue<ToProcessMessage>();

		//these values are changed from a background thread
		long dataMessagesReceivedCounter;
		long dataSizeReceivedCounter;
		uint dataMessagesReceivedChecksum;
		long dataMessagesSentCounter;
		long dataSizeSentCounter;
		uint dataMessagesSentChecksum;

		internal ArrayDataWriter accumulatedMessagesToSend = new ArrayDataWriter();

		///////////////////////////////////////////////

		public class ProfilerDataClass
		{
			//const data
			public DateTime TimeStarted;
			public double WorkingTime;
			public long SystemMessagesReceivedStartCounter;
			public long SystemMessagesSentStartCounter;

			//dynamic data
			public long TotalReceivedMessages;
			public long TotalReceivedSize;
			public long TotalSentMessages;
			public long TotalSentSize;
			public List<ServiceItem> Services = new List<ServiceItem>();

			/////////////////////

			public class ServiceItem
			{
				public List<MessageTypeItem> MessagesByType = new List<MessageTypeItem>();

				//

				public class MessageTypeItem
				{
					public long ReceivedMessages;
					public long ReceivedSize;
					public long SentMessages;
					public long SentSize;

					public struct CustomData
					{
						public long Messages;
						public long Size;
					}
					public Dictionary<string, CustomData> ReceivedCustomData;
					public Dictionary<string, CustomData> SentCustomData;
				}

				//

				public MessageTypeItem GetMessageTypeItem( int identifier )
				{
					while( identifier >= MessagesByType.Count )
						MessagesByType.Add( null );
					var item = MessagesByType[ identifier ];
					if( item == null )
					{
						item = new MessageTypeItem();
						MessagesByType[ identifier ] = item;
					}
					return item;
				}
			}

			/////////////////////

			public ServiceItem GetServiceItem( int identifier )
			{
				while( identifier >= Services.Count )
					Services.Add( null );
				var item = Services[ identifier ];
				if( item == null )
				{
					item = new ServiceItem();
					Services[ identifier ] = item;
				}
				return item;
			}
		}

		///////////////////////////////////////////////

		public static ClientNode[] GetInstances()
		{
			lock( instances )
				return instances.ToArray();
		}

		public void Update()
		{
			OnUpdate();
		}

		public bool Disposed
		{
			get { return disposed; }
		}

		//public bool BeginConnecting
		//{
		//	get { return beginConnecting; }
		//}

		//public bool FailedToConnect
		//{
		//	get { return failedToConnect; }
		//}

		public ProfilerDataClass ProfilerData
		{
			get { return profilerData; }
		}

		public void ProfilerStart( double workingTime )
		{
			ProfilerStop( false );
			profilerData = new ProfilerDataClass();
			profilerData.TimeStarted = DateTime.Now;
			profilerData.WorkingTime = workingTime;
			profilerData.SystemMessagesReceivedStartCounter = DataMessagesReceivedCounter;
			profilerData.SystemMessagesSentStartCounter = DataMessagesSentCounter;

			Log.Info( "Network profiler started." );
		}

		public void ProfilerStop( bool writeToLogs )
		{
			if( profilerData == null )
				return;

			var workedTime = ( DateTime.Now - ProfilerData.TimeStarted ).TotalSeconds;
			if( workedTime > 0 )
			{
				var workedTimeString = workedTime.ToString( "F1" );
				Log.Info( $"Network profiler stopped after {workedTimeString} seconds." );
			}
			else
				Log.Info( $"Network profiler stopped." );

			if( writeToLogs )
				DumpProfilerDataToLogs();

			profilerData = null;
		}

		static string FormatCount( long count )
		{
			return count.ToString( "N0" );
		}

		void DumpProfilerDataToLogs()
		{
			var systemMessagesReceived = DataMessagesReceivedCounter - profilerData.SystemMessagesReceivedStartCounter;
			var systemMessagesSent = DataMessagesSentCounter - profilerData.SystemMessagesSentStartCounter;

			var lines = new List<string>();

			lines.Add( "--------------------------------------------------------------" );
			lines.Add( string.Format( "Total received; {0}", StringUtility.FormatSize( profilerData.TotalReceivedSize ) ) );
			//lines.Add( string.Format( "Total received. Messages: {0}; Size: {1}", FormatCount( profilerData.TotalReceivedMessages ), FormatSize( profilerData.TotalReceivedSize ) ) );

			lines.Add( string.Format( "System messages received; {0}", systemMessagesReceived ) );

			for( int serviceId = 0; serviceId < profilerData.Services.Count; serviceId++ )
			{
				var serviceItem = profilerData.Services[ serviceId ];
				if( serviceItem != null )
				{
					var service = GetService( serviceId );
					lines.Add( string.Format( "> {0}", service.Name ) );

					var messageByTypeItems = new List<(ProfilerDataClass.ServiceItem.MessageTypeItem, int)>();

					for( int messageTypeId = 0; messageTypeId < serviceItem.MessagesByType.Count; messageTypeId++ )
					{
						var messageType = service.GetMessageType( messageTypeId );
						if( messageType != null )
						{
							var messageByTypeItem = serviceItem.GetMessageTypeItem( messageTypeId );
							if( messageByTypeItem != null && messageByTypeItem.ReceivedMessages != 0 )
								messageByTypeItems.Add( (messageByTypeItem, messageTypeId) );
						}
					}

					CollectionUtility.MergeSort( messageByTypeItems, delegate ( (ProfilerDataClass.ServiceItem.MessageTypeItem, int) item1, (ProfilerDataClass.ServiceItem.MessageTypeItem, int) item2 )
					{
						if( item1.Item1.ReceivedSize > item2.Item1.ReceivedSize )
							return -1;
						if( item1.Item1.ReceivedSize < item2.Item1.ReceivedSize )
							return 1;
						return 0;
					} );

					foreach( var messageByTypeItemPair in messageByTypeItems )
					{
						var messageByTypeItem = messageByTypeItemPair.Item1;
						var messageTypeId = messageByTypeItemPair.Item2;

						var messageType = service.GetMessageType( messageTypeId );

						lines.Add( string.Format( "> > {0}; Messages: {1}; Size: {2}", messageType.Name, FormatCount( messageByTypeItem.ReceivedMessages ), StringUtility.FormatSize( messageByTypeItem.ReceivedSize ) ) );

						var customData = messageByTypeItem.ReceivedCustomData;
						if( customData != null )
						{
							var items = new List<(string, ProfilerDataClass.ServiceItem.MessageTypeItem.CustomData)>( customData.Count );
							foreach( var item in customData )
								items.Add( (item.Key, item.Value) );

							CollectionUtility.MergeSort( items, delegate ( (string, ProfilerDataClass.ServiceItem.MessageTypeItem.CustomData) item1, (string, ProfilerDataClass.ServiceItem.MessageTypeItem.CustomData) item2 )
							{
								if( item1.Item2.Size > item2.Item2.Size )
									return -1;
								if( item1.Item2.Size < item2.Item2.Size )
									return 1;
								return 0;
							} );

							foreach( var item in items )
							{
								lines.Add( string.Format( "> > > {0}; Messages: {1}; Size: {2}", item.Item1, FormatCount( item.Item2.Messages ), StringUtility.FormatSize( item.Item2.Size ) ) );
							}
						}
					}


					////for( int messageTypeId = 0; messageTypeId < serviceItem.MessagesByType.Count; messageTypeId++ )
					////{
					////	var messageType = service.GetMessageType( (byte)messageTypeId );
					////	if( messageType != null )
					////	{
					////		var messageByTypeItem = serviceItem.GetMessageTypeItem( messageTypeId );
					////		if( messageByTypeItem != null && messageByTypeItem.ReceivedMessages != 0 )
					////		{
					////			lines.Add( string.Format( "> > {0}; Messages: {1}; Size: {2}", messageType.Name, FormatCount( messageByTypeItem.ReceivedMessages ), FormatSize( messageByTypeItem.ReceivedSize ) ) );

					////			var customData = messageByTypeItem.ReceivedCustomData;
					////			if( customData != null )
					////			{
					////				var items = new List<(string, ProfilerDataClass.ServiceItem.MessageTypeItem.CustomData)>( customData.Count );
					////				foreach( var item in customData )
					////					items.Add( (item.Key, item.Value) );

					////				CollectionUtility.MergeSort( items, delegate ( (string, ProfilerDataClass.ServiceItem.MessageTypeItem.CustomData) item1, (string, ProfilerDataClass.ServiceItem.MessageTypeItem.CustomData) item2 )
					////				{
					////					if( item1.Item2.Size > item2.Item2.Size )
					////						return -1;
					////					if( item1.Item2.Size < item2.Item2.Size )
					////						return 1;
					////					return 0;
					////				} );

					////				foreach( var item in items )
					////				{
					////					lines.Add( string.Format( "> > > {0}; Messages: {1}; Size: {2}", item.Item1, FormatCount( item.Item2.Messages ), FormatSize( item.Item2.Size ) ) );
					////				}
					////			}
					////		}
					////	}
					////}
				}
			}

			lines.Add( "--------------------------------------------------------------" );
			lines.Add( string.Format( "Total sent; Size: {0}", StringUtility.FormatSize( profilerData.TotalSentSize ) ) );
			//lines.Add( string.Format( "Total sent. Messages: {0}; Size: {1}", FormatCount( profilerData.TotalSentMessages ), FormatSize( profilerData.TotalSentSize ) ) );

			lines.Add( string.Format( "System messages sent; {0}", systemMessagesSent ) );

			for( int serviceId = 0; serviceId < profilerData.Services.Count; serviceId++ )
			{
				var serviceItem = profilerData.Services[ serviceId ];
				if( serviceItem != null )
				{
					var service = GetService( serviceId );
					lines.Add( string.Format( "> {0}", service.Name ) );

					var messageByTypeItems = new List<(ProfilerDataClass.ServiceItem.MessageTypeItem, int)>();

					for( int messageTypeId = 0; messageTypeId < serviceItem.MessagesByType.Count; messageTypeId++ )
					{
						var messageType = service.GetMessageType( messageTypeId );
						if( messageType != null )
						{
							var messageByTypeItem = serviceItem.GetMessageTypeItem( messageTypeId );
							if( messageByTypeItem != null && messageByTypeItem.SentMessages != 0 )
								messageByTypeItems.Add( (messageByTypeItem, messageTypeId) );
						}
					}

					CollectionUtility.MergeSort( messageByTypeItems, delegate ( (ProfilerDataClass.ServiceItem.MessageTypeItem, int) item1, (ProfilerDataClass.ServiceItem.MessageTypeItem, int) item2 )
					{
						if( item1.Item1.SentSize > item2.Item1.SentSize )
							return -1;
						if( item1.Item1.SentSize < item2.Item1.SentSize )
							return 1;
						return 0;
					} );

					foreach( var messageByTypeItemPair in messageByTypeItems )
					{
						var messageByTypeItem = messageByTypeItemPair.Item1;
						var messageTypeId = messageByTypeItemPair.Item2;

						var messageType = service.GetMessageType( messageTypeId );

						lines.Add( string.Format( "> > {0}; Messages: {1}; Size: {2}", messageType.Name, FormatCount( messageByTypeItem.SentMessages ), StringUtility.FormatSize( messageByTypeItem.SentSize ) ) );

						var customData = messageByTypeItem.SentCustomData;
						if( customData != null )
						{
							var items = new List<(string, ProfilerDataClass.ServiceItem.MessageTypeItem.CustomData)>( customData.Count );
							foreach( var item in customData )
								items.Add( (item.Key, item.Value) );

							CollectionUtility.MergeSort( items, delegate ( (string, ProfilerDataClass.ServiceItem.MessageTypeItem.CustomData) item1, (string, ProfilerDataClass.ServiceItem.MessageTypeItem.CustomData) item2 )
							{
								if( item1.Item2.Size > item2.Item2.Size )
									return -1;
								if( item1.Item2.Size < item2.Item2.Size )
									return 1;
								return 0;
							} );

							foreach( var item in items )
							{
								lines.Add( string.Format( "> > > {0}; Messages: {1}; Size: {2}", item.Item1, FormatCount( item.Item2.Messages ), StringUtility.FormatSize( item.Item2.Size ) ) );
							}
						}
					}

					////for( int messageTypeId = 0; messageTypeId < serviceItem.MessagesByType.Count; messageTypeId++ )
					////{
					////	var messageType = service.GetMessageType( messageTypeId );
					////	if( messageType != null )
					////	{
					////		var messageByTypeItem = serviceItem.GetMessageTypeItem( messageTypeId );
					////		if( messageByTypeItem != null && messageByTypeItem.SentMessages != 0 )
					////		{
					////			lines.Add( string.Format( "> > {0}; Messages: {1}; Size: {2}", messageType.Name, FormatCount( messageByTypeItem.SentMessages ), FormatSize( messageByTypeItem.SentSize ) ) );

					////			var customData = messageByTypeItem.SentCustomData;
					////			if( customData != null )
					////			{
					////				var items = new List<(string, ProfilerDataClass.ServiceItem.MessageTypeItem.CustomData)>( customData.Count );
					////				foreach( var item in customData )
					////					items.Add( (item.Key, item.Value) );

					////				CollectionUtility.MergeSort( items, delegate ( (string, ProfilerDataClass.ServiceItem.MessageTypeItem.CustomData) item1, (string, ProfilerDataClass.ServiceItem.MessageTypeItem.CustomData) item2 )
					////				{
					////					if( item1.Item2.Size > item2.Item2.Size )
					////						return -1;
					////					if( item1.Item2.Size < item2.Item2.Size )
					////						return 1;
					////					return 0;
					////				} );

					////				foreach( var item in items )
					////				{
					////					lines.Add( string.Format( "> > > {0}; Messages: {1}; Size: {2}", item.Item1, FormatCount( item.Item2.Messages ), FormatSize( item.Item2.Size ) ) );
					////				}
					////			}
					////		}
					////	}
					////}
				}
			}

			lines.Add( "--------------------------------------------------------------" );

			var result = "";
			foreach( var line in lines )
			{
				if( result != "" )
					result += "\r\n";
				result += line;
			}
			Log.Info( result );
		}

		///////////////////////////////////////////////

		public delegate void ProtocolErrorDelegate( ClientNode sender, string message );
		public event ProtocolErrorDelegate ProtocolError;

		public delegate void ConnectionStatusChangedDelegate( ClientNode sender );
		public event ConnectionStatusChangedDelegate ConnectionStatusChanged;

		///////////////////////////////////////////////

		public class ReceivedMessage
		{
			////public string DataString;
			public byte[] DataBinary;
			public string CloseReason;
			public WebSocketCloseStatus? CloseCode;
			public string ErrorMessage;
		}

		///////////////////////////////////////////////

		public class ToProcessMessage
		{
			//public string DataString;

			public bool DataBinary;
			public byte[] DataBinaryArray;
			////public ArraySegment<byte> DataBinarySegment;

			public bool Close;
			public WebSocketCloseStatus CloseStatusCode;
			public string CloseReason;
		}

		///////////////////////////////////////////////

		protected ClientNode()
		{
			lock( instances )
				instances.Add( this );

			servicesReadOnly = new ReadOnlyCollection<ClientService>( services );
		}

		public bool BeginConnect( string host, int port, string clientVersion, string loginData, double keepAliveInSeconds, out string error )
		{
			this.clientVersion = clientVersion;
			this.loginData = loginData;
			this.keepAliveInSeconds = keepAliveInSeconds;

			error = null;

#if !UWP
			if( Disposed )
				Log.Fatal( "NetworkClient: BeginConnect: The client is disposed." );
			if( client != null )
				Log.Fatal( "NetworkClient: BeginConnect: The client is already initialized." );
			if( string.IsNullOrEmpty( host ) )
				Log.Fatal( "NetworkClient: BeginConnect: \"host\" is empty." );

			disconnectionReason = "";

			try
			{
				string welcomeBase64;
				{
					var rootBlock = new TextBlock();
					rootBlock.SetAttribute( "ClientVersion", clientVersion );
					rootBlock.SetAttribute( "LoginData", loginData );
					////foreach( var service in Services )
					////	rootBlock.AddChild( "ClientService", service.Name );
					var welcome = rootBlock.DumpToString( false );
					welcomeBase64 = StringUtility.EncodeToBase64URL( welcome );
				}

				clientConnectHost = host;
				clientConnectPort = port;
				clientConnectAddress = $"ws://{host}:{port}/service/?welcome={welcomeBase64}";
				client = new ClientWebSocket();
				client.Options.KeepAliveInterval = TimeSpan.FromSeconds( keepAliveInSeconds );

				lastStatus = NetworkStatus.Connecting;
				beginConnecting = true;
				//failedToConnect = false;

				thread = new Thread( ThreadFunction );
				thread.IsBackground = true;
				thread.Start();
			}
			catch( Exception e )
			{
				error = e.Message;
				return false;
			}

			return true;
#else
			error = "No network implementation for the platform.";
			return false;
#endif
		}

		[MethodImpl( (MethodImplOptions)512 )]
		void RunReceiveMessages()
		{
			Task.Run( async delegate ()
			{
				var accumulatedBuffer = new ArrayDataWriter( 1024 );
				var buffer = new byte[ 1024 * 16 ];

				while( client.State == WebSocketState.Open )
				{
					var result = await client.ReceiveAsync( new ArraySegment<byte>( buffer ), CancellationToken.None );

					if( result.MessageType != WebSocketMessageType.Close )
					{
						accumulatedBuffer.Write( buffer, 0, result.Count );

						//can check for max message size. get from ServeNode

						if( result.EndOfMessage )
						{
							await OnMessage( result.MessageType, accumulatedBuffer.AsArraySegment() );
							accumulatedBuffer.Reset();
						}
					}
					else
					{
						OnClose( WebSocketCloseStatus.NormalClosure, result.CloseStatusDescription );
						break;
					}
				}
			} );
		}

		[MethodImpl( (MethodImplOptions)512 )]
		async void ThreadFunction( object param )
		{
			//non thread variant?

			if( !beginConnecting || Disposed )
				return;

			try
			{
				await client.ConnectAsync( new Uri( clientConnectAddress ), CancellationToken.None );
			}
			catch( Exception e )
			{
				disconnectionReason = e.Message;
				//failedToConnect = true;
				if( lastStatus != NetworkStatus.Disconnected )
				{
					lastStatus = NetworkStatus.Disconnected;
					OnConnectionStatusChanged();
				}
				return;
			}

			//run task to receive messages
			RunReceiveMessages();

			beginConnecting = false;
			if( lastStatus != NetworkStatus.Connected )
			{
				lastStatus = NetworkStatus.Connected;
				OnConnectionStatusChanged();
			}

			//process messages
			while( !Disposed && !threadNeedExit )
			{
				//send messages
				while( toProcessMessages.TryDequeue( out var message ) )
				{
					if( message.DataBinary )
					{
						//send data message

						try
						{
							var data = message.DataBinaryArray;

							unchecked
							{
								Interlocked.Increment( ref dataMessagesSentCounter );
								Interlocked.Add( ref dataSizeSentCounter, data.Length );
								foreach( var b in data )
									dataMessagesSentChecksum += b;
							}

							if( trace )
								Log.Info( $"Send Binary {data.Length} {DataMessagesSentCounter} {dataMessagesSentChecksum}" );

							await client.SendAsync( new ArraySegment<byte>( data ), WebSocketMessageType.Binary, true, CancellationToken.None );
						}
						catch( Exception e )
						{
							try //!!!!?
							{
								await CloseAsync( WebSocketCloseStatus.ProtocolError, "Unable to send the binary message. " + e.Message );
							}
							catch { }
						}

						//send checksum
						if( DataMessagesSentCounter % 100 == 0 )
						{
							try
							{
								var rootBlock = new TextBlock();
								rootBlock.SetAttribute( "Command", "Checksum" );
								rootBlock.SetAttribute( "Counter", DataMessagesSentCounter.ToString() );
								rootBlock.SetAttribute( "Checksum", dataMessagesSentChecksum.ToString() );
								var text = rootBlock.DumpToString( false );

								if( trace )
									Log.Info( $"Send Text Checksum {DataMessagesSentCounter} {dataMessagesSentChecksum}" );

								var buffer = Encoding.UTF8.GetBytes( text );
								await client.SendAsync( new ArraySegment<byte>( buffer ), WebSocketMessageType.Text, true, CancellationToken.None );
							}
							catch( Exception e )
							{
								await CloseAsync( WebSocketCloseStatus.ProtocolError, "Unable to send the checksum command message. " + e.Message );
								break;
							}
						}
					}
					else if( message.Close )
					{
						//receive close message

						try
						{
							await CloseAsync( message.CloseStatusCode, message.CloseReason );
						}
						catch { }
					}

					if( Disposed || threadNeedExit )
						break;
				}

				threadNeedUpdate.WaitOne();
				//Thread.Sleep( 1 );
			}

			if( threadNeedExit && threadNeedExitSendClose )
			{
				try
				{
					await CloseAsync( WebSocketCloseStatus.NormalClosure, null );

					//var cancellationTokenSource = new CancellationTokenSource( TimeSpan.FromSeconds( 1 ) );
					//var task = CloseAsync( WebSocketCloseStatus.NormalClosure, null, cancellationTokenSource.Token );
					//task.Wait();
				}
				catch { }
			}
		}

		void OnClose( WebSocketCloseStatus closeStatus, string statusDescription )
		{
			receivedMessages.Enqueue( new ReceivedMessage { CloseReason = statusDescription, CloseCode = closeStatus } );

			if( trace )
			{
				var statusDescription2 = statusDescription ?? "(No status)";
				Log.Info( $"OnClose {closeStatus} {statusDescription2}" );
			}
		}

		async Task OnMessage( WebSocketMessageType messageType, ArraySegment<byte> buffer )
		{
			if( messageType == WebSocketMessageType.Text )
			{
				//system commands

				if( trace )
					Log.Info( "OnMessage Text" );

				try
				{
					//? check for hard to parse text block. maybe then not use TextBlock. or use limits when parsing

					var text = Encoding.UTF8.GetString( buffer );
					if( text.Length > 1000 )
						throw new Exception( "The system message is more than 1000 characters." );

					var rootBlock = TextBlock.Parse( text, out var error );
					if( !string.IsNullOrEmpty( error ) )
						throw new Exception( error );

					var command = rootBlock.GetAttribute( "Command" );

					if( command == "Checksum" )
					{
						var counter = long.Parse( rootBlock.GetAttribute( "Counter" ) );
						var checksum = uint.Parse( rootBlock.GetAttribute( "Checksum" ) );

						if( DataMessagesReceivedCounter != counter || dataMessagesReceivedChecksum != checksum )
						{
							throw new Exception( $"Invalid checksum. {DataMessagesReceivedCounter} != {counter} || {dataMessagesReceivedChecksum} != {checksum}" );
						}
					}
					else
						throw new Exception( "Unknown command." );
				}
				catch( Exception ex )
				{
					try
					{
						await CloseAsync( WebSocketCloseStatus.ProtocolError, ex.Message );
					}
					catch { }
				}
			}
			else if( messageType == WebSocketMessageType.Binary )
			{
				//data commands

				var data = buffer.ToArray();
				var length = data.Length;

				unchecked
				{
					Interlocked.Increment( ref dataMessagesReceivedCounter );
					Interlocked.Add( ref dataSizeReceivedCounter, length );
					foreach( var b in data )
						dataMessagesReceivedChecksum += b;
				}

				if( trace )
					Log.Info( $"OnMessage Binary {length} {DataMessagesReceivedCounter} {dataMessagesReceivedChecksum}" );

				receivedMessages.Enqueue( new ReceivedMessage { DataBinary = data } );
			}
		}

		public virtual void Dispose()
		{
			//exit from the thread
			var thread2 = thread;
			if( thread2 != null )
			{
				threadNeedExit = true;
				if( client != null && Status != NetworkStatus.Disconnected )
					threadNeedExitSendClose = true;
				threadNeedUpdate.Set();

				for( var counter = 0; thread2.ThreadState != ThreadState.Stopped && counter < 500; counter++ )
					Thread.Sleep( 1 );
				//for( var counter = 0; thread2.ThreadState != ThreadState.Stopped && counter < 50; counter++ )
				//	Thread.Sleep( 1 );
				////Thread.Sleep( 50 );

				thread = null;
			}

			lastStatus = NetworkStatus.Disconnected;
			beginConnecting = false;

			//clear client field
			if( client != null )
			{
				try
				{
					client.Dispose();
				}
				catch { }

				client = null;
			}

			//dispose services
			foreach( var service in services.ToArray().Reverse() )
				service.PerformDispose();
			//services.Clear();

			lock( instances )
				instances.Remove( this );

			disposed = true;
		}

		public NetworkStatus Status
		{
			get { return lastStatus; }
		}

		public string ClientVersion
		{
			get { return clientVersion; }
		}

		public string LoginData
		{
			get { return loginData; }
		}

		public double KeepAliveInSeconds
		{
			get { return keepAliveInSeconds; }
			//set { keepAliveInSeconds = value; }
		}

		public int SendMessageMaxSize
		{
			get { return sendMessageMaxSize; }
			set { sendMessageMaxSize = value; }
		}

		public int ReceiveMessageMaxSize
		{
			get { return receiveMessageMaxSize; }
			set { receiveMessageMaxSize = value; }
		}

		void ProcessAccumulatedMessagesToSend()
		{
			lock( accumulatedMessagesToSend )
			{
				if( accumulatedMessagesToSend.Length > 0 )
				{
					var array = accumulatedMessagesToSend.ToArray();
					toProcessMessages.Enqueue( new ToProcessMessage { DataBinary = true, DataBinaryArray = array } );
					threadNeedUpdate.Set();
					accumulatedMessagesToSend.Reset();
				}
			}
		}

		[MethodImpl( MethodImplOptions.AggressiveInlining | (MethodImplOptions)512 )]
		internal int AddAccumulatedMessageToSend( ArrayDataWriter writer )
		{
			int bytesWritten;
			lock( accumulatedMessagesToSend )
			{
				var newCount = accumulatedMessagesToSend.Length + writer.Length + 4;
				if( newCount > sendMessageMaxSize )
					ProcessAccumulatedMessagesToSend();

				bytesWritten = accumulatedMessagesToSend.WriteVariableUInt32( (uint)writer.Length );
				accumulatedMessagesToSend.Write( writer.Data, 0, writer.Length );
			}
			return bytesWritten;
		}

		[MethodImpl( (MethodImplOptions)512 )]
		protected virtual void OnUpdate()
		{
			if( ProfilerData != null )
			{
				var workedTime = ( DateTime.Now - ProfilerData.TimeStarted ).TotalSeconds;
				if( workedTime >= ProfilerData.WorkingTime )
					ProfilerStop( true );
			}

#if !UWP

			//process received messages
			while( receivedMessages.TryDequeue( out var message ) )
			{
				lastReceivedMessageTime = EngineApp.EngineTime;

				if( message.DataBinary != null )
				{
					//data binary message

					var data = message.DataBinary;
					var reader = new ArrayDataReader( data );

					while( reader.CurrentPosition < reader.EndPosition )
					{
						var startPosition = reader.CurrentPosition;
						var length = (int)reader.ReadVariableUInt32();
						ProcessReceivedMessage( data, reader.CurrentPosition, length );
						reader.ReadSkip( length );
						var endPosition = reader.CurrentPosition;

						if( profilerData != null )
						{
							profilerData.TotalReceivedMessages++;
							profilerData.TotalReceivedSize += endPosition - startPosition;
						}

						if( reader.Overflow )
						{
							var reason = "OnMessage: Read overflow.";
							OnReceiveProtocolErrorInternal( reason );
							toProcessMessages.Enqueue( new ToProcessMessage { Close = true, CloseStatusCode = WebSocketCloseStatus.ProtocolError, CloseReason = reason } );
							threadNeedUpdate.Set();

							break;
						}
					}
				}
				else if( message.CloseReason != null )
				{
					//close message

					if( trace )
						Log.Info( "OnUpdate: Close. " + message.CloseReason + " " + message.CloseCode.ToString() );

					//update status

					beginConnecting = false;
					if( string.IsNullOrEmpty( disconnectionReason ) )
						disconnectionReason = message.CloseReason ?? "";

					if( lastStatus != NetworkStatus.Disconnected )
					{
						lastStatus = NetworkStatus.Disconnected;
						OnConnectionStatusChanged();
					}

					if( message.CloseCode == WebSocketCloseStatus.ProtocolError )
						OnReceiveProtocolErrorInternal( disconnectionReason );

					//now disconnected
				}
				else if( message.ErrorMessage != null )
				{
					//error message

					var text = message.ErrorMessage;

					//update status
					beginConnecting = false;
					if( lastStatus != NetworkStatus.Disconnected )
					{
						lastStatus = NetworkStatus.Disconnected;
						OnConnectionStatusChanged();
					}

					OnReceiveProtocolErrorInternal( text );

					//now disconnected
				}
			}

			//check for Aborted state
			if( client?.State == WebSocketState.Aborted )
			{
				if( lastStatus != NetworkStatus.Disconnected )
				{
					lastStatus = NetworkStatus.Disconnected;
					disconnectionReason = "Connection is aborted.";
					OnConnectionStatusChanged();
				}
			}

			//update services
			for( int n = 0; n < services.Count; n++ )
				services[ n ].OnUpdate();

			//send accumulated messages
			ProcessAccumulatedMessagesToSend();

#endif
		}

		protected virtual void OnConnectionStatusChanged()
		{
			ConnectionStatusChanged?.Invoke( this );
		}

		public IList<ClientService> Services
		{
			get { return servicesReadOnly; }
		}

		protected void RegisterService( ClientService service )
		{
			if( service.owner != null )
				Log.Fatal( "ClientNode: RegisterService: Service is already registered." );
			if( service.Identifier == 0 )
				Log.Fatal( "ClientNode: RegisterService: Invalid service identifier. Identifier can not be zero." );
			if( service.Identifier > maxServiceIdentifier )
				Log.Fatal( "ClientNode: RegisterService: Invalid service identifier. Max identifier is \"{0}\".", maxServiceIdentifier );

			//check for unique identifier
			{
				var checkService = GetService( service.Identifier );
				if( checkService != null )
					Log.Fatal( "ClientNode: RegisterService: Service with identifier \"{0}\" is already registered.", service.Identifier );
			}

			//check for unique name
			{
				var checkService = GetService( service.Name );
				if( checkService != null )
					Log.Fatal( "ClientNode: RegisterService: Service with name \"{0}\" is already registered.", service.Name );
			}

			service.owner = this;
			services.Add( service );
			servicesByIdentifier[ service.Identifier ] = service;
		}

		[MethodImpl( MethodImplOptions.AggressiveInlining | (MethodImplOptions)512 )]
		public ClientService GetService( int identifier )
		{
			if( identifier >= servicesByIdentifier.Length )
				return null;
			return servicesByIdentifier[ identifier ];
		}

		[MethodImpl( MethodImplOptions.AggressiveInlining | (MethodImplOptions)512 )]
		public virtual ClientService GetService( string name )
		{
			for( int n = 0; n < services.Count; n++ )
			{
				var service = services[ n ];
				if( service.Name == name )
					return service;
			}
			return null;
		}

		[MethodImpl( MethodImplOptions.AggressiveInlining | (MethodImplOptions)512 )]
		public virtual T GetService<T>() where T : ClientService
		{
			for( int n = 0; n < services.Count; n++ )
			{
				var service = services[ n ];
				if( typeof( T ).IsAssignableFrom( service.GetType() ) )
					return (T)service;
			}
			return null;
		}

		//protected internal virtual void OnReceiveProtocolError( string message ) { }

		internal virtual void OnReceiveProtocolErrorInternal( string message )
		{
			if( trace )
				Log.Info( $"OnReceiveProtocolErrorInternal: {message}" );

			if( !Disposed )
				ProtocolError?.Invoke( this, message );
			//OnReceiveProtocolError( message );
		}

		public string DisconnectionReason
		{
			get { return disconnectionReason; }
		}

		public double LastReceivedMessageTime
		{
			get { return lastReceivedMessageTime; }
		}

		[MethodImpl( MethodImplOptions.AggressiveInlining | (MethodImplOptions)512 )]
		internal void ProcessReceivedMessage( byte[] data, int position, int length )
		{
			var reader = new ArrayDataReader( data, position, length );

			var serviceIdentifier = reader.ReadByte();
			var messageIdentifier = reader.ReadByte();

			if( reader.Overflow )
			{
				OnReceiveProtocolErrorInternal( "Invalid message." );
				return;
			}

			//service message
			var service = GetService( serviceIdentifier );
			if( service == null )
			{
				//no such service
				return;
			}

			service.ProcessReceivedMessage( reader, length, messageIdentifier );
		}

		public long DataMessagesReceivedCounter
		{
			get { return Interlocked.Read( ref dataMessagesReceivedCounter ); }
		}

		public long DataSizeReceivedCounter
		{
			get { return Interlocked.Read( ref dataSizeReceivedCounter ); }
		}

		public long DataMessagesSentCounter
		{
			get { return Interlocked.Read( ref dataMessagesSentCounter ); }
		}

		public long DataSizeSentCounter
		{
			get { return Interlocked.Read( ref dataSizeSentCounter ); }
		}

		public string ClientConnectHost
		{
			get { return clientConnectHost; }
		}

		public int ClientConnectPort
		{
			get { return clientConnectPort; }
		}

		public string ClientConnectAddress
		{
			get { return clientConnectAddress; }
		}

		public WebSocket Client
		{
			get { return client; }
		}

		static string ClampCloseReason( string reason )
		{
			var reasonClamped = reason;
			if( reasonClamped.Length > 110 )
				reasonClamped = reason.Substring( 0, 110 ) + "...";
			return reasonClamped;
		}

		async Task CloseAsync( WebSocketCloseStatus status, string rejectReason, CancellationToken cancellationToken = default )
		{
			if( client != null )
				await client.CloseAsync( status, rejectReason != null ? ClampCloseReason( rejectReason ) : null, cancellationToken );
		}
	}
}
#endif