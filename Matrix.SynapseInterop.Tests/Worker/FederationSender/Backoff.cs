using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Matrix.SynapseInterop.Worker.FederationSender;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Matrix.SynapseInterop.Tests.Worker.FederationSender
{
    public class Backoff
    {
        private Matrix.SynapseInterop.Worker.FederationSender.Backoff backoff;

        [SetUp]
        public void Setup()
        {
            backoff = new SynapseInterop.Worker.FederationSender.Backoff();
        }

        [Test]
        public void TesMarkHostIfDown_NoHost()
        {
            Assert.IsFalse(backoff.HostIsDown("localhost"));
        }

        [Test]
        public void TestMarkHostIfDown_IgnoredExceptions()
        {
            Assert.IsFalse(backoff.MarkHostIfDown("localhost", new Exception()));
            Assert.IsFalse(backoff.MarkHostIfDown("localhost", new SocketException((int)SocketError.Fault)));
            Assert.IsFalse(backoff.MarkHostIfDown("localhost", new TransactionFailureException("localhost", HttpStatusCode.Accepted)));
            Assert.IsFalse(backoff.HostIsDown("localhost"));
        }
        
        [Test]
        public void TestMarkHostIfDown_AcceptedExceptions()
        {
            Assert.IsTrue(backoff.MarkHostIfDown("conn_refused", new SocketException((int)SocketError.ConnectionRefused)));
            Assert.IsTrue(backoff.HostIsDown("conn_refused"));
            
            Assert.IsTrue(backoff.MarkHostIfDown("http_ex", new HttpRequestException()));
            Assert.IsTrue(backoff.HostIsDown("http_ex"));
            
            Assert.IsTrue(backoff.MarkHostIfDown("json_ex", new JsonReaderException()));
            Assert.IsTrue(backoff.HostIsDown("json_ex"));
            
            Assert.IsTrue(backoff.MarkHostIfDown("op_ex", new OperationCanceledException()));
            Assert.IsTrue(backoff.HostIsDown("op_ex"));
            
            Assert.IsTrue(backoff.MarkHostIfDown("tx_ex_404", new TransactionFailureException("tx_ex_404", HttpStatusCode.NotFound)));
            Assert.IsTrue(backoff.HostIsDown("tx_ex_404"));
            
            Assert.IsTrue(backoff.MarkHostIfDown("tx_ex_502", new TransactionFailureException("tx_ex_502", HttpStatusCode.BadGateway)));
            Assert.IsTrue(backoff.HostIsDown("tx_ex_502"));
        }
    }
}