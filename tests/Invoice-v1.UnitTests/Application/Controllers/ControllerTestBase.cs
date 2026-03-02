using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace invoice_v1.tests.Controllers
{
    public abstract class ControllerTestBase
    {
        // FIX: Change 'Controller' to 'ControllerBase'
        protected void SetupUser(ControllerBase controller, Guid userId, string role = "Vendor")
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId.ToString()),
                new(ClaimTypes.Role, role)
            };

            var identity = new ClaimsIdentity(claims, "TestAuth");
            var principal = new ClaimsPrincipal(identity);

            // Set the ControllerContext so GetCurrentUserId() works
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            };
        }
    }
}
