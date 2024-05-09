// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using MQTTnet.Client;
using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using MQTTnet.Diagnostics;
using MQTTnet.Internal;
using MQTTnet.Protocol;
using MQTTnet.Packets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.IO;

namespace MQTTnet.TestApp
{
    public static class ClientTest
    {
        private class CertProvider : IMqttClientCertificatesProvider
        {
            private readonly X509Certificate2 privKey;
            private readonly string pemFilePath;

            public CertProvider(X509Certificate2 privKey, string pemFilePath)
            {
                this.privKey = privKey;
                this.pemFilePath = pemFilePath;
            }

            public X509CertificateCollection GetCertificates()
            {
                return new X509CertificateCollection
                {
                    privKey,
                    new X509Certificate2(pemFilePath),
                };
            }
        }

        public static async Task RunAsync()
        {
            try
            {
                var logger = new MqttNetEventLogger();
                MqttNetConsoleLogger.ForwardToConsole(logger);

                var factory = new MqttFactory(logger);
                var client = factory.CreateMqttClient();

                // Certificates 
                var stageId = "f34";

                var deviceName = "CRA_AS_010_01_EXT";

                var pass = "CCcert";

                var pemFilePath = $"{stageId}_{deviceName}_cert.pem";

                var rootCAPath = "rootCA";

                var pfxFilePath = $"{stageId}_{deviceName}_cert.pfx";

                var iotHubName = $"{stageId}-aih-weu-p-001";

                var hostName = $"{iotHubName}.azure-devices.net";

                var userName = $"{hostName}/{deviceName}/?api-version=2021-04-12";

                var msgTopic = $"devices/{deviceName}/messages/events/";

                int port = 8883;

                var privateKey = GetPrivateKey(pfxFilePath, pass, out var combinedCert);

                var tlsOptions = new MqttClientTlsOptions
                {
                    // tls enabled 
                    UseTls = true,
                    CertificateValidationHandler = ValidationHandler,
                    SslProtocol = SslProtocols.Tls12,
                    ClientCertificatesProvider = new CertProvider(combinedCert, pemFilePath),

                };

                var credentials = new MqttClientCredentials(userName, Encoding.UTF8.GetBytes(pass));

                var clientOptions = new MqttClientOptions
                {
                    ChannelOptions = new MqttClientTcpOptions
                    {
                        RemoteEndpoint = new DnsEndPoint(hostName, port),
                        TlsOptions = tlsOptions,
                      
                       
                    },
                    
                    ClientId = deviceName,

                    Credentials = credentials,
                };

                client.ApplicationMessageReceivedAsync += e =>
                {
                    var payloadText = string.Empty;
                    if (e.ApplicationMessage.PayloadSegment.Count > 0)
                    {
                        payloadText = Encoding.UTF8.GetString(
                            e.ApplicationMessage.PayloadSegment.Array,
                            e.ApplicationMessage.PayloadSegment.Offset,
                            e.ApplicationMessage.PayloadSegment.Count);
                    }
                    
                    Console.WriteLine("### RECEIVED APPLICATION MESSAGE ###");
                    Console.WriteLine($"+ Topic = {e.ApplicationMessage.Topic}");
                    Console.WriteLine($"+ Payload = {payloadText}");
                    Console.WriteLine($"+ QoS = {e.ApplicationMessage.QualityOfServiceLevel}");
                    Console.WriteLine($"+ Retain = {e.ApplicationMessage.Retain}");
                    Console.WriteLine();
                    
                    return CompletedTask.Instance;
                };

                client.ConnectedAsync += async e =>
                {
                    Console.WriteLine("### CONNECTED WITH SERVER ###");

                    await client.SubscribeAsync("#");

                    Console.WriteLine("### SUBSCRIBED ###");
                };

                client.DisconnectedAsync += async e =>
                {
                    Console.WriteLine("### DISCONNECTED FROM SERVER ###");
                    await Task.Delay(TimeSpan.FromSeconds(5));

                    try
                    {
                        await client.ConnectAsync(clientOptions);
                    }
                    catch
                    {
                        Console.WriteLine("### RECONNECTING FAILED ###");
                    }
                };

                try
                {
                    await client.ConnectAsync(clientOptions);
                }
                catch (Exception exception)
                {
                    Console.WriteLine("### CONNECTING FAILED ###" + Environment.NewLine + exception);
                }

                Console.WriteLine("### WAITING FOR APPLICATION MESSAGES ###");

                while (true)
                {
                    Console.ReadLine();

                    await client.SubscribeAsync("test");

                    var applicationMessage = new MqttApplicationMessageBuilder()
                        .WithTopic(msgTopic)
                        .WithPayload("Hello World")
                        // Qos1
                        .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                        .WithRetainFlag(false)
                        .Build();

                    await client.PublishAsync(applicationMessage);
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception);
            }
        }

        private static bool ValidationHandler(MqttClientCertificateValidationEventArgs args)
        {
            return true;
        }

        private static RSA GetPrivateKey(string pfxFilePath, string pass , out X509Certificate2 cert)
        {
            cert = new X509Certificate2(pfxFilePath, pass, X509KeyStorageFlags.Exportable);

            return cert.GetRSAPrivateKey();

        }
    }
}
