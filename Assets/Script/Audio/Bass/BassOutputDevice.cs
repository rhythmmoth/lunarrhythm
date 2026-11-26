using ManagedBass;
using System;
using YARG.Core.Audio;
using YARG.Core.Logging;

namespace YARG.Audio.BASS
{
    public sealed class BassOutputDevice : OutputDevice
    {
        public readonly int DeviceId;

#nullable enable
        internal static BassOutputDevice? Create(int deviceId, string name)
#nullable disable
        {
            try
            {
                if (!Bass.Init(deviceId, 44100, DeviceInitFlags.Default | DeviceInitFlags.Latency, IntPtr.Zero))
                {
                    if (Bass.LastError != Errors.Already)
                    {
                        YargLogger.LogFormatError("Failed to initialize BASS device: {0}!", Bass.LastError);

                        return null;
                    }
                }

                // Inside the try: after a crash the device can come back "Already" initialised with no current
                // device on this thread, and a throw here would stop the whole startup (no settings, no menus)
                return new BassOutputDevice(deviceId >= 0 ? deviceId : Bass.CurrentDevice, name);
            }
            catch (BassException e)
            {
                YargLogger.LogException(e);
                return null;
            }
        }

        public BassOutputDevice Use()
        {
            Bass.CurrentDevice = DeviceId;

            return this;
        }

        private BassOutputDevice(int deviceId, string name)
            : base(name)
        {
            DeviceId = deviceId;
            Use();
        }

        protected override void DisposeManagedResources()
        {
            Bass.CurrentDevice = DeviceId;
            Bass.Free();
        }
    }
}
