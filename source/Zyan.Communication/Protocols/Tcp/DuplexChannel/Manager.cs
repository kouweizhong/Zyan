using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Diagnostics;
using System.Net.Sockets;
using System.Collections;
using System.Text.RegularExpressions;
using Zyan.Communication.Protocols.Tcp.DuplexChannel.Diagnostics;

namespace Zyan.Communication.Protocols.Tcp.DuplexChannel
{
	internal class Manager
	{
		#region Uri Utilities
		static readonly Regex regUrl = new Regex("tcpex://(?<server>[^/]+)/?(?<objectID>.*)", RegexOptions.Compiled);

		public static string Parse(string url, out string objectID)
		{
			Match m = regUrl.Match(url);
			if (m.Success)
			{
				objectID = m.Groups["objectID"].Value;
				return m.Groups["server"].Value;
			}
			else
			{
				objectID = null;
				return null;
			}
		}

		public static string[] GetUrlsForUri(string objectUri, int port, Guid guid)
		{
			if (objectUri == null)
				objectUri = "/";
			else if (objectUri == "" || objectUri[0] != '/')
				objectUri = "/" + objectUri;

			ArrayList retVal = new ArrayList();

			if (guid != Guid.Empty)
				retVal.Add(string.Format("tcpex://{0}{1}", guid, objectUri));

			string hostname = Dns.GetHostName();
			IPHostEntry hostEntry = Dns.GetHostEntry(hostname);
			if (port != 0)
			{
				foreach (IPAddress address in hostEntry.AddressList)
					retVal.Add(string.Format("tcpex://{0}:{1}{2}", address, port, objectUri));
			}

			return (string[])retVal.ToArray(typeof(string));
		}

		public static string[] GetAddresses(int port, Guid guid)
		{
			ArrayList retVal = new ArrayList();
			if (guid != Guid.Empty)
				retVal.Add(string.Format("{0}", guid));

			string hostname = Dns.GetHostName();
			IPHostEntry hostEntry = Dns.GetHostEntry(hostname);
			if (port != 0)
			{
				foreach (IPAddress address in hostEntry.AddressList)
					if (port != 0)
						retVal.Add(string.Format("{0}:{1}", address, port));
				if (!retVal.Contains(string.Format("{0}:{1}", IPAddress.Loopback, port)))
					retVal.Add(string.Format("{0}:{1}", IPAddress.Loopback, port));
			}

			return (string[])retVal.ToArray(typeof(string));
		}

		static readonly Regex regServer = new Regex("(?<address>[^:]+):(?<port>.+)", RegexOptions.Compiled);
		static string ResolveHostName(string server)
		{
			if (server == null)
				return "";

			Match m = regServer.Match(server);
			if (!m.Success)
				return server;
			else
				return string.Format("{0}:{1}", GetHostByName(m.Groups["address"].Value), m.Groups["port"]);
		}

		public static string CreateUrl(Guid guid)
		{
			return string.Format("tcpex://{0}", guid);
		}

		public static IPAddress GetHostByName(string name)
		{
			foreach (var ip in Dns.GetHostEntry(name).AddressList)
			{
				if (ip.AddressFamily == AddressFamily.InterNetwork)
					return ip;
			}

			throw new ArgumentOutOfRangeException("IPv4 address not found for host " + name);
		}
		#endregion

		#region Listening
		// Hashtable<Guid|string, AsyncResult>
		static readonly Hashtable listeners = new Hashtable();

		public static void StartListening(Connection connection)
		{
			Message.BeginReceive(connection, new AsyncCallback(ReceiveMessage), null);
		}

		public static int StartListening(int port, TcpExChannel channel)
		{
			Socket listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			listener.Bind(new IPEndPoint(IPAddress.Any, port));
			listener.Listen(1000);
			listener.BeginAccept(new AsyncCallback(listener_Accept), new object[] {listener, channel});

			return ((IPEndPoint)listener.LocalEndPoint).Port;
		}

		static void listener_Accept(IAsyncResult ar)
		{
			object[] state = (object[])ar.AsyncState;
			Socket listener = (Socket)state[0];
			TcpExChannel channel = (TcpExChannel)state[1];
			Socket client = listener.EndAccept(ar);

			try
			{
				StartListening(Connection.RegisterConnection(client, channel));
			}
			catch (DuplicateConnectionException)
			{
			}

			listener.BeginAccept(new AsyncCallback(listener_Accept), new object[] {listener, channel});
		}

		// Hashtable<string(server), Stack<object[Connection, Message]>>
		static readonly Hashtable outstandingMessages = new Hashtable();

		internal static void ReceiveMessage(IAsyncResult ar)
		{
			Message m = null;
			Connection connection = null;

			try
			{
				m = Message.EndReceive(out connection, ar);
			}
			catch (MessageException e)
			{
				TriggerException(e);
				return;
			}

			lock (listeners)
			{
				if (!listeners.Contains(m.Guid))
				{
					// New incoming message
					if (listeners.Contains(connection.LocalGuid))
					{
						AsyncResult myAr = (AsyncResult)listeners[connection.LocalGuid];
						listeners.Remove(connection.LocalGuid);
						myAr.Complete(connection, m);
					}
					else if (listeners.Contains(connection.LocalAddress))
					{
						AsyncResult myAr = (AsyncResult)listeners[connection.LocalAddress];
						listeners.Remove(connection.LocalAddress);
						myAr.Complete(connection, m);
					}
					else
					{
						Stack outstanding = (Stack)outstandingMessages[connection.LocalAddress];
						if (outstanding == null)
						{
							outstanding = new Stack();
							outstandingMessages.Add(connection.LocalAddress, outstanding);
						}
						outstanding.Push(new Object[] {connection, m});
					}	
				}
				else
				{
					// Response to previous message
					AsyncResult myAr = (AsyncResult)listeners[m.Guid];
					listeners.Remove(m.Guid);
					myAr.Complete(connection, m);
				}
			}
		}

		static void TriggerException(MessageException e)
		{
			lock (listeners)
			{
				ArrayList toBeDeleted = new ArrayList();

				foreach (object key in listeners.Keys)
				{
					AsyncResult myAr = (AsyncResult)listeners[key];
					if (myAr != null && myAr.Connection == e.Connection)
					{
						myAr.Complete(e);
						toBeDeleted.Add(myAr);
					}
				}
				foreach (object o in toBeDeleted)
					listeners.Remove(o);
			}
			e.Connection.Close();
		}
		#endregion

		#region ReadMessage
		public static IAsyncResult BeginReadMessage(object guidOrServer, Connection connection, AsyncCallback callback, object asyncState)
		{
			lock (listeners)
			{
				Debug.Assert(!listeners.Contains(guidOrServer), "Handler for this guid already registered.");

				AsyncResult ar = new AsyncResult(connection, callback, asyncState);
				if (outstandingMessages.Contains(guidOrServer))
				{
					Stack outstanding = (Stack)outstandingMessages[guidOrServer];
					if (outstanding.Count > 0)
					{
						object[] result = (object[])outstanding.Pop();
						ar.Complete((Connection)result[0], (Message)result[1]);
						return ar;
					}
				}
				
				listeners.Add(guidOrServer, ar);
				return ar;
			}
		}

		public static Message EndReadMessage(out Connection connection, IAsyncResult ar)
		{
			AsyncResult myAr = (AsyncResult)ar;
			if (myAr.Failed)
				throw myAr.Exception;
			connection = myAr.Connection;
			return myAr.Message;
		}

		public static Message EndReadMessage(out Connection connection, out Exception exception, IAsyncResult ar)
		{
			AsyncResult myAr = (AsyncResult)ar;
			exception = myAr.Exception;
			connection = myAr.Connection;
			return myAr.Message;
		}

		public static Message ReadMessage(Connection connection, object guidOrServer)
		{
			IAsyncResult ar = BeginReadMessage(guidOrServer, connection, null, null);
			ar.AsyncWaitHandle.WaitOne();
			return EndReadMessage(out connection, ar);
		}
		#endregion

		#region AsyncResult
		class AsyncResult : IAsyncResult
		{
			object asyncState;
			AsyncCallback callback;
			bool isCompleted = false;
			System.Threading.ManualResetEvent waitHandle;
			Message m;
			Connection connection;
			Exception exception;

			public AsyncResult(Connection connection, AsyncCallback callback, object asyncState)
			{
				this.connection = connection;
				this.callback = callback;
				this.asyncState = asyncState;
			}

			#region Complete
			public void Complete(Connection connection, Message m)
			{
				lock (this)
				{
					if (isCompleted)
						throw new InvalidOperationException("Already complete");
					this.m = m;
					this.connection = connection;

					isCompleted = true;
					if (waitHandle != null)
						waitHandle.Set();
					if (callback != null)
						ThreadPool.QueueUserWorkItem(new WaitCallback(DoCallback), this);
				}
			}

			public void Complete(Exception e)
			{
				lock (this)
				{
					if (isCompleted)
						throw new InvalidOperationException("Already complete");

					exception = e;

					isCompleted = true;
					if (waitHandle != null)
						waitHandle.Set();
					if (callback != null)
						ThreadPool.QueueUserWorkItem(new WaitCallback(DoCallback), this);
				}
			}

			void DoCallback(object o)
			{
				callback(this);
			}
			#endregion

			#region Properties
			public bool Failed
			{
				get
				{
					return exception != null;
				}
			}

			public Exception Exception
			{
				get
				{
					return exception;
				}
			}

			public Message Message
			{
				get
				{
					return m;
				}
			}

			public Connection Connection
			{
				get
				{
					return connection;
				}
			}
			#endregion
		
			#region Implementation of IAsyncResult
			public object AsyncState
			{
				get
				{
					return asyncState;
				}
			}

			public bool CompletedSynchronously
			{
				get
				{
					return false;
				}
			}

			public System.Threading.WaitHandle AsyncWaitHandle
			{
				get
				{
					lock (this)
					{
						if (waitHandle == null)
							waitHandle = new System.Threading.ManualResetEvent(isCompleted);
						return waitHandle;
					}
				}
			}

			public bool IsCompleted
			{
				get
				{
					return isCompleted;
				}
			}
			#endregion
		}
		#endregion
	}
}