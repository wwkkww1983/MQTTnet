﻿using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet.Diagnostics;
using MQTTnet.Packets;

namespace MQTTnet.Server
{
    public class MqttClientKeepAliveMonitor
    {
        private readonly Stopwatch _lastPacketReceivedTracker = new Stopwatch();
        private readonly Stopwatch _lastNonKeepAlivePacketReceivedTracker = new Stopwatch();

        private readonly IMqttNetChildLogger _logger;
        private readonly string _clientId;
        private readonly Action _callback;

        private bool _isPaused;
        private Task _workerTask;

        public MqttClientKeepAliveMonitor(string clientId, Action callback, IMqttNetChildLogger logger)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            _clientId = clientId;
            _callback = callback;
            _logger = logger.CreateChildLogger(nameof(MqttClientKeepAliveMonitor));
        }

        public TimeSpan LastPacketReceived => _lastPacketReceivedTracker.Elapsed;

        public TimeSpan LastNonKeepAlivePacketReceived => _lastNonKeepAlivePacketReceivedTracker.Elapsed;

        public void Start(int keepAlivePeriod, CancellationToken cancellationToken)
        {
            if (keepAlivePeriod == 0)
            {
                return;
            }

            _workerTask = Task.Run(() => RunAsync(keepAlivePeriod, cancellationToken), cancellationToken);
        }

        public void Pause()
        {
            _isPaused = true;
        }

        public void Resume()
        {
            _isPaused = false;
        }

        public void PacketReceived(MqttBasePacket packet)
        {
            _lastPacketReceivedTracker.Restart();

            if (!(packet is MqttPingReqPacket))
            {
                _lastNonKeepAlivePacketReceivedTracker.Restart();
            }
        }

        private async Task RunAsync(int keepAlivePeriod, CancellationToken cancellationToken)
        {
            try
            {
                _lastPacketReceivedTracker.Restart();
                _lastNonKeepAlivePacketReceivedTracker.Restart();

                while (!cancellationToken.IsCancellationRequested)
                {
                    // Values described here: [MQTT-3.1.2-24].
                    if (!_isPaused && _lastPacketReceivedTracker.Elapsed.TotalSeconds > keepAlivePeriod * 1.5D)
                    {
                        _logger.Warning(null, "Client '{0}': Did not receive any packet or keep alive signal.", _clientId);
                        _callback();
    
                        return;
                    }

                    await Task.Delay(keepAlivePeriod, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Client '{0}': Unhandled exception while checking keep alive timeouts.", _clientId);
            }
            finally
            {
                _logger.Verbose("Client {0}: Stopped checking keep alive timeout.", _clientId);
            }
        }
    }
}