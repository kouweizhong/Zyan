﻿using System;
using System.IO;
using System.Reflection;
using Zyan.Communication;
using Zyan.Communication.Toolbox;

namespace IntegrationTest_DistributedEvents
{
	public class RequestResponseResult
	{
		string _testName = string.Empty;

		public RequestResponseResult(string testName)
		{
			Count = 0;
			_testName = testName;
		}

		public int Count { get; set; }

		public void ReceiveResponseSingleCall(string text)
		{
			Console.WriteLine(string.Format("[{1}] Request/Response: {0}", text,_testName));
			Count++;
		}
	}

	class Program
	{
		private static AppDomain _serverAppDomain;

		public static int Main(string[] args)
		{
			AppDomainSetup setup = new AppDomainSetup();
			setup.ApplicationBase = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

			_serverAppDomain = AppDomain.CreateDomain("Server", null, setup);
			_serverAppDomain.Load(typeof(ZyanConnection).Assembly.GetName());

			CrossAppDomainDelegate serverWork = new CrossAppDomainDelegate(() =>
			{
				var server = EventServer.Instance;
				if (server != null)
				{
					Console.WriteLine("Event server started.");
				}
			});
			_serverAppDomain.DoCallBack(serverWork);

			// Subscriptions are tested synchronously
			ZyanSettings.LegacyBlockingSubscriptions = true;

			// Test IPC Binary
			int ipcBinaryTestResult = IpcBinaryTest.RunTest();
			Console.WriteLine("Passed: {0}", ipcBinaryTestResult == 0);

			// Test TCP Binary
			int tcpBinaryTestResult = TcpBinaryTest.RunTest();
			Console.WriteLine("Passed: {0}", tcpBinaryTestResult == 0);

			// Test TCP Custom
			int tcpCustomTestResult = TcpCustomTest.RunTest();
			Console.WriteLine("Passed: {0}", tcpCustomTestResult == 0);

			// Test TCP Duplex
			int tcpDuplexTestResult = TcpDuplexTest.RunTest();
			Console.WriteLine("Passed: {0}", tcpDuplexTestResult == 0);

			// Test HTTP Custom
			int httpCustomTestResult = HttpCustomTest.RunTest();
			Console.WriteLine("Passed: {0}", httpCustomTestResult == 0);

			// Test NULL Channel
			const string nullChannelResultSlot = "NullChannelResult";
			_serverAppDomain.DoCallBack(new CrossAppDomainDelegate(() =>
			{
				int result = NullChannelTest.RunTest();
				AppDomain.CurrentDomain.SetData(nullChannelResultSlot, result);
			}));
			var nullChannelTestResult = Convert.ToInt32(_serverAppDomain.GetData(nullChannelResultSlot));
			Console.WriteLine("Passed: {0}", nullChannelTestResult == 0);

			// Stop the event server
			EventServerLocator locator = _serverAppDomain.CreateInstanceAndUnwrap(Assembly.GetExecutingAssembly().FullName, "IntegrationTest_DistributedEvents.EventServerLocator") as EventServerLocator;
			locator.GetEventServer().Dispose();
			Console.WriteLine("Event server stopped.");

			if (!MonoCheck.IsRunningOnMono || MonoCheck.IsUnixOS)
			{
				// Mono/Windows bug:
				// AppDomain.Unload freezes in Mono under Windows if tests for
				// System.Runtime.Remoting.Channels.Tcp.TcpChannel were executed.
				AppDomain.Unload(_serverAppDomain);
				Console.WriteLine("Server AppDomain unloaded.");
			}

			if (ipcBinaryTestResult + tcpBinaryTestResult + tcpCustomTestResult + tcpDuplexTestResult + httpCustomTestResult + nullChannelTestResult == 0)
			{
				Console.WriteLine("All tests passed.");
				return 0;
			}

			return 1;
		}
	}
}
