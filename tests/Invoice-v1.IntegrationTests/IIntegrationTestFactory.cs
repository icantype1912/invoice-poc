using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http;

namespace Invoice_v1.IntegrationTests;

// This public interface allows the Test Classes to stay Public
public interface IIntegrationTestFactory
{
    HttpClient CreateClient();
    IServiceProvider Services { get; }
}