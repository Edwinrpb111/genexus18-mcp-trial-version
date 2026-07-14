using System.Net;
using Xunit;

namespace GxMcp.Gateway.Tests
{
    // Regression for the false-promotion chain that killed live workers:
    // a routine id-less JSON-RPC notification gets a spec-correct HTTP 204 / empty body
    // from the master; the proxy used to read that as "empty response → master dead",
    // retry 3x, force a lease takeover, and its port-recovery then hard-killed the real
    // master's whole process tree (GeneXus worker included). The empty body must be
    // accepted as success for notifications / 204, and only be a fault for an id-bearing
    // request that gets nothing back.
    public class ProxyPromotionTests
    {
        [Theory]
        [InlineData(true, HttpStatusCode.NoContent, true)]   // notification, 204 → accept
        [InlineData(true, HttpStatusCode.OK, true)]          // notification, 200 empty → accept
        [InlineData(false, HttpStatusCode.NoContent, true)]  // request, explicit 204 → accept
        [InlineData(false, HttpStatusCode.OK, false)]        // request, empty 200 → real fault
        [InlineData(false, HttpStatusCode.Accepted, false)]  // request, empty 202 → real fault
        public void ProxyEmptyBodyIsSuccess_Classifies(bool isNotification, HttpStatusCode status, bool expected)
        {
            Assert.Equal(expected, Program.ProxyEmptyBodyIsSuccess(isNotification, status));
        }
    }
}
