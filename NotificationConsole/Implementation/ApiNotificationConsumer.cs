using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using RestSharp.Portable;
using RestSharp.Portable.HttpClient;
using Wow.Notification.Api.Shared.Model;

namespace Wow.Mailers.Unicorn.Implementation
{
    public class NotificationApiConsumer
    {
        private string NotificationApiBaseUrl { get; }
        private string OauthTokenUrl { get; }
        private string ClientId { get; }
        private string SharedSecret { get; }
        private string Scope { get; set; }

        private static string _oauthToken = null;
        private static readonly object SyncRoot = new Object();

        private static string OauthToken
        {
            get => _oauthToken;
            set
            {
                lock (SyncRoot)
                {
                    _oauthToken = value;
                }
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NotificationApiConsumer"/> class.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        public NotificationApiConsumer(IConfigurationRoot configuration)
        {
            //NotificationApiBaseUrl = "https://internal.dev-ecn.clm.wowinc.com/notification-api/api/"; 
            NotificationApiBaseUrl = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOTIFICATION_API_URL")) ? configuration["AppSettings:NotificationApiBaseUrl"] : Environment.GetEnvironmentVariable("NOTIFICATION_API_URL");
            OauthTokenUrl = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OAUTH_NOTIFICATION_URL")) ? configuration["OauthNotification:OauthTokenUrl"] : Environment.GetEnvironmentVariable("OAUTH_NOTIFICATION_URL");
            ClientId = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OAUTH_NOTIFICATION_CLIENT_ID")) ? configuration["OauthNotification:ClientId"] : Environment.GetEnvironmentVariable("OAUTH_NOTIFICATION_CLIENT_ID");
            SharedSecret = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OAUTH_NOTIFICATION_SECRET")) ? configuration["OauthNotification:SharedSecret"] : Environment.GetEnvironmentVariable("OAUTH_NOTIFICATION_SECRET");
            Scope = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OAUTH_NOTIFICATION_SCOPE")) ? configuration["OauthNotification:Scope"] : Environment.GetEnvironmentVariable("OAUTH_NOTIFICATION_SCOPE");

            if (OauthToken == null)
                GetOauthToken();
        }

        /// <summary>
        /// Gets the oauth token.
        /// </summary>
        /// <exception cref="System.ApplicationException"></exception>
        private void GetOauthToken()
        {
            var client = new RestClient(new Uri(OauthTokenUrl));
            var request = new RestRequest(Method.POST);
            //request.AddHeader("Content-Type", "application/x-www-form-urlencoded");
            request.AddHeader("ContentType", "text/xml");
            request.AddHeader("cache-control", "no-cache");
            request.AddParameter("grant_type", "client_credentials");
            request.AddParameter("client_secret", SharedSecret);
            request.AddParameter("client_id", ClientId);
            request.AddParameter("scope", Scope);
            IRestResponse response = client.Execute(request).Result;

            if (response.IsSuccess)
            {
                var token = JsonConvert.DeserializeObject<Dictionary<string, object>>(response.Content)["access_token"].ToString();
                OauthToken = token;
            }
            else
                throw new ApplicationException($"Not able to get the authorization token from the identity server: {response.Content}");
        }

        /// <summary>
        /// Sends the email notification.
        /// </summary>
        /// <param name="emailRequest">The email request.</param>
        /// <returns></returns>
        public (bool taskStatus, string confirmationId, string errorMessage) SendEmailNotification(EmailNotificationRequest emailRequest)
        {
            try
            {
                int numberExecutions = 0;

                while (numberExecutions < 2)
                {
                    using (var client = new RestClient(new Uri(NotificationApiBaseUrl)) { IgnoreResponseStatusCode = true })
                    {
                        var request = new RestRequest("notifications/email", Method.POST);
                        request.AddHeader("Authorization", $"Bearer {OauthToken}");
                        request.AddBody(emailRequest);
                        var response = client.Execute(request).Result;

                        if (response.IsSuccess)
                            return (true, response.Content, string.Empty);

                        if (response.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            GetOauthToken();
                            numberExecutions++;
                            continue;

                        }
                        return (false, null, $"Not able to create email notification: {response.Content}");
                    }
                }

                return (false, null, $"Not able to get the response from the server due to the invalid token");
            }
            catch (Exception e)
            {
                Console.WriteLine(e.InnerException);
                Console.WriteLine(emailRequest.ToString());
                return (false, null, e.Message);
            }
        }

        /// <summary>
        /// Sends the SMS notification.
        /// </summary>
        /// <param name="smsRequest">The SMS request.</param>
        /// <returns></returns>
        public (bool taskStatus, string confirmationId, string errorMessage) SendSmsNotification(SmsNotificationRequest smsRequest)
        {
            try
            {
                int numberExecutions = 0;

                while (numberExecutions < 2)
                {
                    using (var client = new RestClient(new Uri(NotificationApiBaseUrl)) { IgnoreResponseStatusCode = true })
                    {
                        var request = new RestRequest("notifications/sms", Method.POST);
                        request.AddHeader("Authorization", $"Bearer {OauthToken}");
                        request.AddBody(smsRequest);
                        var response = client.Execute(request).Result;

                        if (response.IsSuccess)
                            return (true, response.Content, string.Empty);

                        if (response.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            GetOauthToken();
                            numberExecutions++;
                            continue;
                        }

                        return (false, null, $"Not able to create sms notification: {response.Content}");
                    }
                }

                return (false, null, $"Not able to get the response from the server due to the invalid token");
            }
            catch (Exception e)
            {
                return (false, null, e.Message);
            }
        }

        /// <summary>
        /// Sends the dialer notification.
        /// </summary>
        /// <param name="dialerRequest">The dialer request.</param>
        /// <returns></returns>
        public (bool taskStatus, string confirmationId, string errorMessage) SendDialerNotification(DialerNotificationRequest dialerRequest)
        {
            try
            {
                int numberExecutions = 0;

                while (numberExecutions < 2)
                {
                    using (var client = new RestClient(new Uri(NotificationApiBaseUrl)) { IgnoreResponseStatusCode = true })
                    {
                        var request = new RestRequest("notifications/dialer", Method.POST);
                        request.AddHeader("Authorization", $"Bearer {OauthToken}");
                        request.AddBody(dialerRequest);
                        var response = client.Execute(request).Result;

                        if (response.IsSuccess)
                            return (true, response.Content, string.Empty);

                        if (response.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            GetOauthToken();
                            numberExecutions++;
                            continue;
                        }

                        return (false, null, $"Not able to create dialer notification: {response.Content}");
                    }
                }

                return (false, null, $"Not able to get the response from the server due to the invalid token");
            }
            catch (Exception e)
            {
                return (false, null, e.Message);
            }
        }
    }
}