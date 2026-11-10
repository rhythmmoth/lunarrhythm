#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using ManagedBass;
using ManagedBass.Mix;
using YARG.Core.Audio;
using YARG.Core.Logging;
using YARG.Settings;

namespace YARG.Audio.BASS
{
    /// <summary>
    /// A sample channel that uses BASS to play VOX files.
    ///
    /// Unlike all the others, this one will automatically queue samples and play them sequentially.
    /// </summary>
    public sealed class BassVoxSampleChannel : VoxSampleChannel
    {
        private static readonly List<BassVoxSampleChannel>  Channels = new();
        private static readonly Queue<BassVoxSampleChannel> Queue    = new();
        private static readonly Random                      Rng      = new();
        private readonly        int[]                       _sampleHandles;
        private static          bool                        _queueActive;

#nullable enable
        /// <param name="paths">Every take of the sample; one is picked at random each time it plays.</param>
        public static BassVoxSampleChannel? Create(VoxSample sample, IReadOnlyList<string> paths, OutputChannel? outputChannel)
#nullable disable
        {
            var handles = new List<int>();
            var channels = new List<int>();
            foreach (string path in paths)
            {
                int handle = Bass.SampleLoad(path, 0, 0, 2, BassFlags.Decode);
                if (handle == 0)
                {
                    YargLogger.LogFormatError("Failed to load {0} {1}: {2}!", sample, path, Bass.LastError);
                    continue;
                }

                int channel = Bass.SampleGetChannel(handle);
                if (channel == 0)
                {
                    Bass.SampleFree(handle);
                    YargLogger.LogFormatError("Failed to create {0} channel: {1}!", sample, Bass.LastError);
                    continue;
                }

                // TODO: This should probably have its own volume setting at some point
                if (!Bass.ChannelSetAttribute(channel, ChannelAttribute.Volume, 1.0f))
                {
                    YargLogger.LogFormatError("Failed to set {0} volume: {1}!", sample, Bass.LastError);
                }

                handles.Add(handle);
                channels.Add(channel);
            }

            if (handles.Count == 0)
            {
                return null;
            }

            return new BassVoxSampleChannel(handles.ToArray(), channels.ToArray(), sample, paths[0], outputChannel);
        }

        private static void QueuePlayback(BassVoxSampleChannel channel)
        {
            Queue.Enqueue(channel);
            if (!_queueActive)
            {
                PlayQueued();
            }
        }

        private static async void PlayQueued()
        {
            _queueActive = true;
            while (Queue.TryDequeue(out var channel))
            {
                await UniTask.WaitUntil(() => !IsAnyPlaying());
                channel.Play();
            }
            _queueActive = false;
        }

        private static bool IsAnyPlaying()
        {
            foreach (var channel in Channels)
            {
                if (channel.IsPlaying())
                {
                    return true;
                }
            }

            return false;
        }

        private readonly int[]  _channels;
        private          int    _channel;

#nullable enable
        private BassVoxSampleChannel(int[] handles, int[] channels, VoxSample sample, string path, OutputChannel? outputChannel)
            : base(sample, path)
#nullable disable
        {
            _sampleHandles = handles;
            _channels = channels;
            _channel = channels[0];
            SetOutputChannel_Internal(outputChannel);
            Channels.Add(this);
            SetVolume_Internal(GlobalAudioHandler.GetTrueVolume(SongStem.VoxSample));
        }

        protected override void Play_Internal()
        {
            // Don't particularly like doing it here, but this is the only place in the playback chain where we can
            // check for the vox enabled setting
            if (!SettingsManager.Settings.EnableVoxSamples.Value)
            {
                return;
            }

            if (IsAnyPlaying())
            {
                QueuePlayback(this);
                return;
            }

            _channel = _channels[Rng.Next(_channels.Length)];
            if (!Bass.ChannelPlay(_channel, true))
            {
                YargLogger.LogFormatError("Failed to play {0} channel: {1}!", Sample, Bass.LastError);
            }
        }

        protected override void SetVolume_Internal(double volume)
        {
            volume *= AudioHelpers.SfxSamples[(int) Sample].Volume;
            foreach (int channel in _channels)
            {
                if (!Bass.ChannelSetAttribute(channel, ChannelAttribute.Volume, volume))
                {
                    YargLogger.LogFormatError("Failed to set {0} volume: {1}!", Sample, Bass.LastError);
                }
            }
        }

#nullable enable
        protected override void SetOutputChannel_Internal(OutputChannel? channel)
#nullable disable
        {
            foreach (int c in _channels)
            {
                BassHelpers.UpdateOutputChannels(c, channel);
            }
        }

        protected override bool IsPlaying_Internal()
        {
            return Bass.ChannelIsActive(_channel) == PlaybackState.Playing;
        }

        protected override void DisposeUnmanagedResources()
        {
            foreach (int handle in _sampleHandles)
            {
                Bass.SampleFree(handle);
            }
        }
    }
}