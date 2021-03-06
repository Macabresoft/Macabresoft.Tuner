﻿namespace Macabresoft.Zvukosti.Library {

    using Macabresoft.Core;
    using Macabresoft.Zvukosti.Library.Input;
    using System;

    /// <summary>
    /// Monitors the frequency of a <see cref="IWaveIn" /> device.
    /// </summary>
    public sealed class FrequencyMonitor : PropertyChangedNotifier, IDisposable {

        /// <summary>
        /// The highest frequency this monitor can detect. May need to be increased or customizable
        /// if the tuner begins supporting custom tunings.
        /// </summary>
        public const float HighestFrequency = 1500f;

        /// <summary>
        /// The lowest frequency this monitor can detect.
        /// </summary>
        public const float LowestFrequency = 20f;

        private readonly int _highPeriod;
        private readonly object _lock = new object();
        private readonly int _lowPeriod;
        private readonly RollingMeanFloat _rollingAverageFrequency = new RollingMeanFloat(5);
        private readonly ISampleProvider _sampleProvider;
        private float _frequency;
        private bool _isDisposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="FrequencyMonitor" /> class.
        /// </summary>
        public FrequencyMonitor(ISampleProvider sampleProvider) {
            this._sampleProvider = sampleProvider ?? throw new ArgumentNullException(nameof(sampleProvider));
            this._sampleProvider.SamplesAvailable += this.SampleProvider_SamplesAvailable;
            this._lowPeriod = (int)Math.Floor(this.SampleRate / FrequencyMonitor.HighestFrequency);
            this._highPeriod = (int)Math.Ceiling(this.SampleRate / FrequencyMonitor.LowestFrequency);
        }

        /// <summary>
        /// Gets the frequency in Hz.
        /// </summary>
        /// <value>The frequency in Hz.</value>
        public float Frequency {
            get {
                return this._frequency;
            }

            private set {
                this.Set(ref this._frequency, value);
            }
        }

        /// <summary>
        /// Gets the sample rate this frequency monitor is currently using.
        /// </summary>
        /// <value>The sample rate this frequency monitor is currently using.</value>
        public int SampleRate {
            get {
                return this._sampleProvider.SampleRate;
            }
        }

        /// <inheritdoc />
        public void Dispose() {
            if (!this._isDisposed) {
                this._sampleProvider.SamplesAvailable -= this.SampleProvider_SamplesAvailable;
                this._isDisposed = true;
            }

            GC.SuppressFinalize(this);
        }

        private BufferInformation GetBufferInformation(float[] samples) {
            if (samples.Length < this._highPeriod) {
                throw new InvalidOperationException("The sample rate isn't large enough for the buffer length.");
            }

            var greatestMagnitude = float.NegativeInfinity;
            var chosenPeriod = -1;

            for (var period = this._lowPeriod; period < this._highPeriod; period++) {
                var sum = 0f;
                for (var i = 0; i < samples.Length - period; i++) {
                    sum += samples[i] * samples[i + period];
                }

                var newMagnitude = sum / samples.Length;
                if (newMagnitude > greatestMagnitude) {
                    chosenPeriod = period;
                    greatestMagnitude = newMagnitude;
                }
            }

            var frequency = (float)this.SampleRate / chosenPeriod;
            return frequency < LowestFrequency || frequency > HighestFrequency ?
                BufferInformation.Unknown :
                new BufferInformation((float)frequency, greatestMagnitude);
        }

        private void SampleProvider_SamplesAvailable(object sender, SamplesAvailableEventArgs e) {
            if (e.Samples.Length > 0 && e.Samples[^2] != 0f) {
                lock (this._lock) {
                    var bufferInformation = this.GetBufferInformation(e.Samples);
                    this._rollingAverageFrequency.Add(bufferInformation.Frequency);
                    this.Frequency = this._rollingAverageFrequency.MeanValue;
                    this.Frequency = bufferInformation.Frequency;
                }
            }
        }
    }
}