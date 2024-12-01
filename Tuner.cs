using System;
using System.Linq;
using System.Threading;
using System.Numerics;
using NAudio.Wave;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using System.ComponentModel;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;


namespace GuitarTools;

public class Tuner : INotifyPropertyChanged
{
    private const int SampleFreq = 48000; // Sample frequency in Hz
    private const int WindowSize = 48000; // Window size of the DFT in samples
    private const int WindowStep = 12000; // Step size of window
    private const int NumHps = 5; // Max number of harmonic product spectrums
    private const double PowerThresh = 1e-6; // Tuning is activated if the signal power exceeds this threshold
    private const double ConcertPitch = 440; // Defining A4
    private const double WhiteNoiseThresh = 0.2; // Threshold for noise suppression
    //private readonly double _windowTLen = WindowSize / (double)SampleFreq; // Length of the window in seconds
    //private readonly double _sampleTLength = 1.0 / SampleFreq; // Length between two samples in seconds
    private readonly double _deltaFreq = SampleFreq / (double)WindowSize; // Frequency step width
    private readonly int[] _octaveBands = [50, 100, 200, 400, 800, 1600, 3200, 6400, 12800, 25600];
    private readonly string[] _allNotes = ["A", "A#", "B", "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#"];
    private float[] _windowSamples = new float[WindowSize];
    private string[] _noteBuffer = new string[2];
    private readonly double[] _hannWindow = Window.Hann(WindowSize);
    private WaveInEvent waveIn;
    private readonly DispatcherQueue _dispatcherQueue;

    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsTuning { get; private set; }
    private ClosestNote? _currentNote;
    public ClosestNote? CurrentNote
    {
        get => _currentNote;
        private set
        {
            if (_currentNote == value) return;
            _currentNote = value;
            OnPropertyChanged(nameof(CurrentNote));
        }
    }

    public Tuner()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _noteBuffer[0] = "1";
        _noteBuffer[1] = "2";
    }

    protected virtual void OnPropertyChanged(string propertyName)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        else
        {
            _dispatcherQueue.TryEnqueue(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)));
        }
    }
    /// <summary>
    /// Starts the tuner.
    /// </summary>
    public void Start()
    {
        if (IsTuning)
            return;

        IsTuning = true;
        waveIn = new WaveInEvent();
        waveIn.WaveFormat = new WaveFormat(SampleFreq, 16, 1);
        waveIn.BufferMilliseconds = (int)(WindowStep / (double)SampleFreq * 1000.0);
        waveIn.DataAvailable += WaveIn_DataAvailable;
        waveIn.StartRecording();
    }
    /// <summary>
    /// Stops the tuner.
    /// </summary>
    public void Stop()
    {
        if (!IsTuning)
            return;

        IsTuning = false;

        if (waveIn != null)
        {
            waveIn.DataAvailable -= WaveIn_DataAvailable;
            waveIn.StopRecording();
            waveIn.Dispose();
            waveIn = null;
        }

        SetCurrentNoteSafe(null);
    }
    
    /// <summary>
    ///  Finds the closest musical note for a given pitch.
    /// </summary>
    /// <param name="pitch"></param>
    /// <returns></returns>
    private Tuple<string, double> FindClosestNote(double pitch)
    {
        var i = (int)Math.Round(12 * Math.Log2(pitch / ConcertPitch));
        var closestNote = _allNotes[(i + 1200) % 12] + (4 + ((i + 9) / 12));
        var closestPitch = ConcertPitch * Math.Pow(2, i / 12.0);
        return Tuple.Create(closestNote, closestPitch);
    }
    private void WaveIn_DataAvailable(object sender, WaveInEventArgs e)
    {
        const int bytesPerSample = 2; // 16 bits per sample
        var samplesRecorded = e.BytesRecorded / bytesPerSample;
        var newSamples = new float[samplesRecorded];
        for (var i = 0; i < samplesRecorded; i++)
        {
            var sample = BitConverter.ToInt16(e.Buffer, i * bytesPerSample);
            newSamples[i] = sample / 32768f; // Normalize to [-1.0, 1.0]
        }

        // Append new samples to windowSamples
        if (_windowSamples.Length < WindowSize)
        {
            _windowSamples = _windowSamples.Concat(newSamples).ToArray();
        }
        else
        {
            // Shift samples
            var samplesToRemove = newSamples.Length;
            Array.Copy(_windowSamples, samplesToRemove, _windowSamples, 0, _windowSamples.Length - samplesToRemove);
            Array.Copy(newSamples, 0, _windowSamples, _windowSamples.Length - samplesToRemove, samplesToRemove);
        }

        // Calculate signal power
        double signalPower = _windowSamples.Select(s => s * s).Sum() / _windowSamples.Length;
        
        if (signalPower < PowerThresh)
        {
            return;
        }

        // Apply Hann window
        var hannSamples = new double[_windowSamples.Length];
        for (var i = 0; i < _windowSamples.Length; i++)
        {
            hannSamples[i] = _windowSamples[i] * _hannWindow[i];
        }

        // Perform FFT
        var fftBuffer = hannSamples.Select(v => new Complex(v, 0)).ToArray();
        Fourier.Forward(fftBuffer, FourierOptions.Matlab);

        // Compute magnitude spectrum
        var magnitudeSpec = new double[fftBuffer.Length / 2];
        for (var i = 0; i < magnitudeSpec.Length; i++)
        {
            magnitudeSpec[i] = fftBuffer[i].Magnitude;
        }

        // Suppress mains hum, set everything below 62Hz to zero
        var limit = (int)(62 / _deltaFreq);
        for (var i = 0; i < limit && i < magnitudeSpec.Length; i++)
        {
            magnitudeSpec[i] = 0;
        }

        // Noise suppression based on octave bands
        for (var j = 0; j < _octaveBands.Length - 1; j++)
        {
            var indStart = (int)(_octaveBands[j] / _deltaFreq);
            var indEnd = (int)(_octaveBands[j + 1] / _deltaFreq);
            indEnd = indEnd < magnitudeSpec.Length ? indEnd : magnitudeSpec.Length;
            double avgEnergyPerFreq = 0;
            for (var i = indStart; i < indEnd; i++)
            {
                avgEnergyPerFreq += magnitudeSpec[i] * magnitudeSpec[i];
            }
            avgEnergyPerFreq = Math.Sqrt(avgEnergyPerFreq / (indEnd - indStart));
            for (var i = indStart; i < indEnd; i++)
            {
                magnitudeSpec[i] = magnitudeSpec[i] > WhiteNoiseThresh * avgEnergyPerFreq ? magnitudeSpec[i] : 0;
            }
        }

        // Interpolate spectrum
        var ipolLength = magnitudeSpec.Length * NumHps;
        var magSpecIpol = new double[ipolLength];
        for (var i = 0; i < ipolLength; i++)
        {
            var x = i / (double)NumHps;
            var x0 = (int)Math.Floor(x);
            var x1 = x0 + 1;
            if (x1 >= magnitudeSpec.Length)
            {
                x1 = magnitudeSpec.Length - 1;
            }
            var y0 = magnitudeSpec[x0];
            var y1 = magnitudeSpec[x1];
            magSpecIpol[i] = y0 + (y1 - y0) * (x - x0);
        }

        // Normalize interpolated spectrum
        var normFactor = Math.Sqrt(magSpecIpol.Select(v => v * v).Sum());
        for (var i = 0; i < magSpecIpol.Length; i++)
        {
            magSpecIpol[i] /= normFactor;
        }

        // Compute Harmonic Product Spectrum (HPS)
        var hpsSpec = magSpecIpol.ToArray();
        for (var i = 1; i < NumHps; i++)
        {
            var decimateFactor = i + 1;
            var length = hpsSpec.Length / decimateFactor;
            for (var j = 0; j < length; j++)
            {
                hpsSpec[j] *= magSpecIpol[j * decimateFactor];
            }
        }

        // Find the maximum in HPS spectrum
        var maxInd = Array.IndexOf(hpsSpec, hpsSpec.Max());
        var maxFreq = maxInd * (SampleFreq / (double)WindowSize) / NumHps;

        var (closestNote, closestPitch) = FindClosestNote(maxFreq);
        maxFreq = Math.Round(maxFreq, 1);
        closestPitch = Math.Round(closestPitch, 1);

        // Update note buffer
        _noteBuffer = new[] { closestNote, _noteBuffer[0] };

        if (_noteBuffer.All(n => n == _noteBuffer[0]))
        {
            SetCurrentNoteSafe(new ClosestNote
            {
                Note = closestNote,
                MaxFrequency = maxFreq,
                ClosestPitch = closestPitch
            });
            Console.WriteLine($"Note: {closestNote}, Max Frequency: {maxFreq} Hz, Closest Pitch: {closestPitch} Hz");
        }
    }
    private void SetCurrentNoteSafe(ClosestNote? note)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            CurrentNote = note;
        }
        else
        {
            _dispatcherQueue.TryEnqueue(() => CurrentNote = note);
        }
    }
}

public class ClosestNote
{
    /// <summary>
    /// The closest musical note.
    /// </summary>
    public string? Note { get; set; }
    /// <summary>
    /// The maximum frequency in Hz.
    /// </summary>
    public double MaxFrequency { get; set; }
    /// <summary>
    /// The closest pitch in Hz.
    /// </summary>
    public double ClosestPitch { get; set; }
    public string MaxFrequencyText => MaxFrequency > 0 ? $"Frequency: {MaxFrequency} Hz" : "Frequency: N/A";
    public string ClosestPitchText => ClosestPitch > 0 ? $"Closest Pitch: {ClosestPitch} Hz" : "Closest Pitch: N/A";
}