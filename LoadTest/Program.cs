using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;



namespace LoadTestApp


{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Choose a load test:");
            Console.WriteLine("1. Transaction Load Test");
            Console.WriteLine("2. User Registration Load Test");
            var choice = Console.ReadLine();

            switch (choice)
            {
                case "1":
                    await TransactionLoadTest();
                    break;
                case "2":
                    await UserRegistrationLoadTest();
                    break;
                default:
                    Console.WriteLine("Invalid choice.");
                    break;
            }
        }

        static async Task TransactionLoadTest()
        {
         int transactionCount = 100;
            string apiUrl = "http://localhost:5276/api/transactions/create"; // Change if needed

            var tasks = new List<Task<HttpResponseMessage>>();
            var client = new HttpClient();

            // Prepare your transaction payload here
            string payload = @"{
                ""fromWalletId"": ""00000000-0000-0000-0000-000000000001"",
                ""toWalletId"": ""00000000-0000-0000-0000-000000000002"",
                ""amount"": 0.01,
                ""currency"": 0
            }";

            var content = new StringContent(payload, Encoding.UTF8, "application/json");

            var start = DateTime.UtcNow;

            for (int i = 0; i < transactionCount; i++)
            {
                // Each request should have its own content instance
                var reqContent = new StringContent(payload, Encoding.UTF8, "application/json");
                tasks.Add(client.PostAsync(apiUrl, reqContent));
            }

            var responses = await Task.WhenAll(tasks);

            var end = DateTime.UtcNow;
            double seconds = (end - start).TotalSeconds;

            int success = 0, fail = 0;
            var failReasons = new List<string>();
            for (int i = 0; i < responses.Length; i++)
            {
                var resp = responses[i];
                if (resp.IsSuccessStatusCode)
                    success++;
                else
                {
                    fail++;
                    string reason = $"Status: {(int)resp.StatusCode} {resp.StatusCode}";
                    string body = await resp.Content.ReadAsStringAsync();
                    failReasons.Add($"Request #{i + 1}: {reason}\nBody: {body}");
                }
            }

            Console.WriteLine($"Sent {transactionCount} requests in {seconds:F2} seconds");
            Console.WriteLine($"Success: {success}, Fail: {fail}");
            Console.WriteLine($"Throughput: {transactionCount / seconds:F2} tx/sec");
            if (failReasons.Count > 0)
            {
                Console.WriteLine("\nFailure reasons:");
                foreach (var reason in failReasons)
                {
                    Console.WriteLine(reason);
                    Console.WriteLine("----------------------");
                }
            }
        }

        

        static async Task UserRegistrationLoadTest()
        {
           int userCount = 100;
            string apiUrl = "http://localhost:5276/api/auth/register"; // Adjust if needed

            var tasks = new List<Task<HttpResponseMessage>>();
            var client = new HttpClient();

            var start = DateTime.UtcNow;

            for (int i = 0; i < userCount; i++)
            {
                string email = $"testuser{i}_{Guid.NewGuid():N}@example.com";
                string userName = $"testuser{i}_{Guid.NewGuid():N}";
                string password = "Test@12345";
               

                string payload = $@"{{
                    ""email"": ""{email}"",
                    ""password"": ""{password}"",
                    ""userName"": ""{userName}""
                }}";

                var reqContent = new StringContent(payload, Encoding.UTF8, "application/json");
                tasks.Add(client.PostAsync(apiUrl, reqContent));
            }

            var responses = await Task.WhenAll(tasks);

            var end = DateTime.UtcNow;
            double seconds = (end - start).TotalSeconds;

            int success = 0, fail = 0;
            var failReasons = new List<string>();
            for (int i = 0; i < responses.Length; i++)
            {
                var resp = responses[i];
                if (resp.IsSuccessStatusCode)
                    success++;
                else
                {
                    fail++;
                    string reason = $"Status: {(int)resp.StatusCode} {resp.StatusCode}";
                    string body = await resp.Content.ReadAsStringAsync();
                    failReasons.Add($"Request #{i + 1}: {reason}\nBody: {body}");
                }
            }

            Console.WriteLine($"Sent {userCount} registration requests in {seconds:F2} seconds");
            Console.WriteLine($"Success: {success}, Fail: {fail}");
            Console.WriteLine($"Throughput: {userCount / seconds:F2} users/sec");
            if (failReasons.Count > 0)
            {
                Console.WriteLine("\nFailure reasons:");
                foreach (var reason in failReasons)
                {
                    Console.WriteLine(reason);
                    Console.WriteLine("----------------------");
                }
            }
        }
    }
}