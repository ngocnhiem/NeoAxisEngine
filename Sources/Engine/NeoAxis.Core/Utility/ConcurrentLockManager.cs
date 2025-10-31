// Copyright (C) NeoAxis Group Ltd. 8 Copthall, Roseau Valley, 00152 Commonwealth of Dominica.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace NeoAxis
{
	public class ConcurrentLockManager<T>
	{
		readonly ConcurrentDictionary<T, object> locks = new ConcurrentDictionary<T, object>();

		/////////////////////

		class Releaser : IDisposable
		{
			readonly object lockObject;

			public Releaser( object lockObject )
			{
				this.lockObject = lockObject;
			}

			public void Dispose()
			{
				Monitor.Exit( lockObject );
			}
		}

		/////////////////////

		public IDisposable LockDisposable( T key )
		{
			var lockObject = locks.GetOrAdd( key, _ => new object() );
			Monitor.Enter( lockObject );
			return new Releaser( lockObject );
		}
	}
}
