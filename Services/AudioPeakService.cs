using System;
using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace Moonrise.Services
{
    public interface IAudioPeakService : IDisposable
    {
        float GetVolumePeak();
    }

    public sealed class AudioPeakService : IAudioPeakService
    {
        private MMDeviceEnumerator? _enumerator;
        private MMDevice? _device;
        private AudioSessionControl? _currentSession;

        public float GetVolumePeak()
        {
            try
            {
                if (_enumerator == null)
                    _enumerator = new MMDeviceEnumerator();

                var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
                if (_device == null || _device.ID != defaultDevice.ID)
                {
                    _device?.Dispose();
                    _device = defaultDevice;
                    _currentSession = null;
                }
                else
                {
                    defaultDevice.Dispose();
                }

                if (_currentSession == null || _currentSession.State != NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive)
                    _currentSession = FindCurrentProcessSession();

                if (_currentSession != null)
                    return _currentSession.AudioMeterInformation.MasterPeakValue;
            }
            catch
            {
                _currentSession = null;
            }

            return 0f;
        }

        private AudioSessionControl? FindCurrentProcessSession()
        {
            if (_device == null)
            {
                return null;
            }

            var sessionManager = _device.AudioSessionManager;
            var sessions = sessionManager.Sessions;
            int currentPid = Environment.ProcessId;

            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (session.GetProcessID == currentPid)
                {
                    return session;
                }
                session.Dispose();
            }

            return null;
        }

        public void Dispose()
        {
            _currentSession?.Dispose();
            _device?.Dispose();
            _enumerator?.Dispose();
        }
    }
}
