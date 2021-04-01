﻿// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.  See License.txt in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OData.TestCommon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using Xunit;

namespace Microsoft.AspNetCore.OData.E2E.Tests.Routing.QueryRequest
{
    public class DollarQueryTests : WebApiTestBase<DollarQueryTests>
    {
        private const string CustomersResourcePath = "odata/DollarQueryCustomers";
        private const string SingleCustomerResourcePath = "odata/DollarQueryCustomers(1)";
        private const string ApplicationJsonODataMinimalMetadataStreamingTrue = "application/json;odata.metadata=minimal;odata.streaming=true";
        private const string ApplicationJsonODataMinimalMetadataStreamingFalse = "application/json;odata.metadata=minimal;odata.streaming=false";

        public DollarQueryTests(WebApiTestFixture<DollarQueryTests> fixture)
            : base(fixture)
        {
        }

        protected static void UpdateConfigureServices(IServiceCollection services)
        {
            services.ConfigureControllers(typeof(DollarQueryCustomersController));
            services.AddOData(opt => opt.AddModel("odata", GetEdmModel()).Count().Filter().OrderBy().Expand().SetMaxTop(null).Select());
        }

        protected static void UpdateConfigure(IApplicationBuilder app)
        {
            // Add OData /$query middleware
            app.UseODataQueryRequest();

            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }

        public static TheoryDataSet<string, string> ODataQueryOptionsData
        {
            get
            {
                var odataQueryOptionsData = new TheoryDataSet<string, string>();

                foreach (var tuple in 
                    new[]{
                        new Tuple<string, string>(CustomersResourcePath, "$filter=Id le 5"),
                        new Tuple<string, string>(CustomersResourcePath, "$filter=contains(Name, '3')"),
                        new Tuple<string, string>(CustomersResourcePath, "$orderby=Id desc"),
                        new Tuple<string, string>(CustomersResourcePath, "$top=1"),
                        new Tuple<string, string>(CustomersResourcePath, "$top=1&$skip=3"),
                        new Tuple<string, string>(CustomersResourcePath, "$orderby=Id desc&top=2&skip=3"),
                        new Tuple<string, string>(CustomersResourcePath, "$select=Id,Name"),
                        new Tuple<string, string>(CustomersResourcePath, "$expand=Orders"),
                        new Tuple<string, string>(CustomersResourcePath, "$select=Orders&$expand=Orders"),
                        new Tuple<string, string>(CustomersResourcePath, "$format=" + Uri.EscapeDataString(ApplicationJsonODataMinimalMetadataStreamingFalse)),
                        new Tuple<string, string>(CustomersResourcePath, "$expand=SpecialOrder($select=Detail)&$filter=Id le 5&$orderby=Id desc&$select=Id&$format=" + Uri.EscapeDataString(ApplicationJsonODataMinimalMetadataStreamingFalse)),
                        new Tuple<string, string>(SingleCustomerResourcePath, "$select=Id,Name"),
                        new Tuple<string, string>(SingleCustomerResourcePath, "$expand=Orders"),
                        new Tuple<string, string>(SingleCustomerResourcePath, "$format=" + Uri.EscapeDataString(ApplicationJsonODataMinimalMetadataStreamingFalse)),
                        new Tuple<string, string>(SingleCustomerResourcePath, "$expand=SpecialOrder($select=Detail)&$select=Id&$format=" + Uri.EscapeDataString(ApplicationJsonODataMinimalMetadataStreamingFalse))
                    })
                {
                    odataQueryOptionsData.Add(tuple.Item1, tuple.Item2);
                }

                return odataQueryOptionsData;
            }
        }

        private static IEdmModel GetEdmModel()
        {
            ODataConventionModelBuilder builder = new ODataConventionModelBuilder();
            builder.EntitySet<DollarQueryCustomer>("DollarQueryCustomers");
            builder.EntitySet<DollarQueryOrder>("DollarQueryOrders");
            return builder.GetEdmModel();
        }

        [Theory]
        [MemberData(nameof(ODataQueryOptionsData))]
        public async Task ODataQueryOptionsInRequestBody_ForSupportedMediaType(string resourcePath, string queryOptionsPayload)
        {
            // Arrange
            string requestUri = resourcePath + "/$query";
            var contentType = "text/plain";

            var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
            request.Content = new StringContent(queryOptionsPayload);
            request.Content.Headers.ContentType = new MediaTypeWithQualityHeaderValue(contentType);
            request.Content.Headers.ContentLength = queryOptionsPayload.Length;

            request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(ApplicationJsonODataMinimalMetadataStreamingTrue));
            HttpClient client = CreateClient();

            // Act
            var response = await client.SendAsync(request);

            // Arrange
            Assert.True(response.IsSuccessStatusCode);
        }

        [Fact]
        public async Task ODataQueryOptionsInRequestBody_ReturnsExpectedResult()
        {
            // Arrange
            string requestUri = CustomersResourcePath + "/$query";
            var contentType = "text/plain";
            var queryOptionsPayload = "$filter=Id eq 1";

            var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
            request.Content = new StringContent(queryOptionsPayload);
            request.Content.Headers.ContentType = new MediaTypeWithQualityHeaderValue(contentType);
            request.Content.Headers.ContentLength = queryOptionsPayload.Length;

            request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(ApplicationJsonODataMinimalMetadataStreamingTrue));
            HttpClient client = CreateClient();

            // Act
            var response = await client.SendAsync(request);

            // Assert
            Assert.True(response.IsSuccessStatusCode);
            Assert.Contains("\"value\":[{\"Id\":1,\"Name\":\"Customer Name 1\"}]", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task ODataQueryOptionsInRequestBody_PlusQueryOptionsOnRequestUrl()
        {
            // Arrange
            string requestUri = CustomersResourcePath + "/$query?$orderby=Id desc";
            var contentType = "text/plain";
            string payload = "$filter=Id eq 1 or Id eq 9";

            var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
            request.Content = new StringContent(payload);
            request.Content.Headers.ContentType = new MediaTypeWithQualityHeaderValue(contentType);
            request.Content.Headers.ContentLength = payload.Length;

            request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(ApplicationJsonODataMinimalMetadataStreamingTrue));
            HttpClient client = CreateClient();

            // Act
            var response = await client.SendAsync(request);

            // Assert
            Assert.True(response.IsSuccessStatusCode);

            Assert.Contains("\"value\":[{\"Id\":9,\"Name\":\"Customer Name 9\"},{\"Id\":1,\"Name\":\"Customer Name 1\"}]",
                await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task ODataQueryOptionsInRequestBody_RepeatedOnRequestUrl()
        {
            // Arrange
            string requestUri = CustomersResourcePath + "/$query?$filter=Id eq 1";
            var contentType = "text/plain";
            string payload = "$filter=Id eq 1";

            var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
            request.Content = new StringContent(payload);
            request.Content.Headers.ContentType = new MediaTypeWithQualityHeaderValue(contentType);
            request.Content.Headers.ContentLength = payload.Length;

            request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(ApplicationJsonODataMinimalMetadataStreamingTrue));
            HttpClient client = CreateClient();

            // Act
            var response = await client.SendAsync(request);

            // Assert
            Assert.False(response.IsSuccessStatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task ODataQueryOptionsInRequestBody_ForUnsupportedMediaType()
        {
            // Arrange
            string requestUri = CustomersResourcePath + "/$query";
            var contentType = "application/xml";
            var queryOptionsPayload = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><QueryOptions><filter>Id le 5</filter></QueryOptions>";

            var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
            request.Content = new StringContent(queryOptionsPayload);
            request.Content.Headers.ContentType = new MediaTypeWithQualityHeaderValue(contentType);
            request.Content.Headers.ContentLength = queryOptionsPayload.Length;

            request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(ApplicationJsonODataMinimalMetadataStreamingTrue));
            HttpClient client = CreateClient();

            // Act
            var response = await client.SendAsync(request);

            // Assert
            Assert.False(response.IsSuccessStatusCode);
        }

        [Fact]
        public async Task ODataQueryOptionsInRequestBody_Empty()
        {
            // Arrange
            string requestUri = CustomersResourcePath + "/$query";
            var contentType = "text/plain";

            var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
            request.Content = new StringContent("");
            request.Content.Headers.ContentType = new MediaTypeWithQualityHeaderValue(contentType);
            request.Content.Headers.ContentLength = 0;

            request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(ApplicationJsonODataMinimalMetadataStreamingTrue));
            HttpClient client = CreateClient();

            // Act
            var response = await client.SendAsync(request);

            // Assert
            Assert.True(response.IsSuccessStatusCode);
        }

        [Fact(Skip = "It seems the 8192 can't make the long query options failing, need more investigation!")]
        public async Task ODataQueryOptionsInRequestBody_MitigateLimitViolationOnLongUrl()
        {
            // KestrelServerLimits.MaxRequestLineSize equals 8192 so setting query string length to that value
            // should be enough to make us breach the threshold
            // NOTE: Change of limit could cause test to start failing
            var queryStringLength = 8192;

            // Arrange
            var baseUri = CustomersResourcePath;
            var builder = new StringBuilder();
            var loremIpsum = "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.";

            builder.AppendFormat("$filter=Name eq '{0}", loremIpsum);
            // Ensure that query string breaches the threshold
            do
            {
                builder.AppendFormat(" {0}", loremIpsum);
            }
            while (builder.Length < queryStringLength);

            builder.Append("'");
            var queryOptionsString = builder.ToString();

            var getRequest = new HttpRequestMessage(HttpMethod.Get, baseUri + '?' + queryOptionsString);
            getRequest.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(ApplicationJsonODataMinimalMetadataStreamingTrue));
            HttpClient client = CreateClient();

            // Act
            var getResponse = await client.SendAsync(getRequest);

            // Assert: Should fail because threshold is exceeed
            Assert.False(getResponse.IsSuccessStatusCode);

            // Arrange: Now send the same query string in the request body
            var postRequest = new HttpRequestMessage(HttpMethod.Post, baseUri + "/$query");
            var contentType = "text/plain";
            postRequest.Content = new StringContent(queryOptionsString);
            postRequest.Content.Headers.ContentType = new MediaTypeWithQualityHeaderValue(contentType);

            // Act
            var postResponse = await client.SendAsync(postRequest);

            // Assert: Should pass because the query options were sent in the request body
            Assert.True(postResponse.IsSuccessStatusCode);
        }
    }
}