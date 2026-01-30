using CommonConfigurations;
using LoanBroker;
using LoanBroker.Messages;
using Microsoft.Extensions.Hosting;

Host.CreateApplicationBuilder(args)
    .ConfigureServices()
    .ConfigureAzureNServiceBusEndpoint("LoanBroker", c => c.Routing.RouteToEndpoint(typeof(FindBestLoanWithScore), "LoanBroker"))
    .Build()
    .Run();