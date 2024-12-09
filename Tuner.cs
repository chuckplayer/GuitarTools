using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;


namespace GuitarTools;

/// <summary>
/// Inspiration and starting code for this was taken from the Python HPS algorithm for guitar tuning by chciken.com
/// Check out the original article here: https://chionophilous.wordpress.com/2012/04/09/harmonic-product-spectrum-guitar-tuner/
/// The original code can be found here: https://github.com/not-chciken/guitar_tuner 
/// </summary>
public class Tuner : INotifyPropertyChanged
{
    private const int SampleFreq = 48000; // Sample frequency in Hz
    private const int WindowSize = 48000; // Window size of the DFT in samples
    private const int WindowStep = 12000; // Step size of window
    private const int NumHps = 5; // Max number of harmonic product spectrums
    private const double PowerThresh = 1e-6; // Tuning is activated if the signal power exceeds this threshold
    private const double ConcertPitch = 440; // Defining A4
    private const double WhiteNoiseThresh = 0.2; // Threshold for noise suppression
    private const double DeltaFreq = SampleFreq / (double)WindowSize; // Frequency step width
    private readonly int[] _octaveBands = [50, 100, 200, 400, 800, 1600, 3200, 6400, 12800, 25600];
    private readonly string[] _allNotes = ["A", "A#", "B", "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#"];
    private float[] _windowSamples = new float[WindowSize];
    private string[] _noteBuffer = new string[2];
    private readonly double[] _hannWindow = Window.Hann(WindowSize);
    private WaveInEvent _waveIn;
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
        _waveIn = new WaveInEvent();
        _waveIn.WaveFormat = new WaveFormat(SampleFreq, 16, 1);
        _waveIn.BufferMilliseconds = (int)(WindowStep / (double)SampleFreq * 1000.0);
        _waveIn.DataAvailable += WaveIn_DataAvailable;
        _waveIn.StartRecording();
    }
    /// <summary>
    /// Stops the tuner.
    /// </summary>
    public void Stop()
    {
        if (!IsTuning)
            return;

        IsTuning = false;

        _waveIn!.DataAvailable -= WaveIn_DataAvailable;
        _waveIn.StopRecording();
        _waveIn.Dispose();
        CurrentNote = null;
    }
    /// <summary>
    /// Gets the available tunings.
    /// </summary>
    /// <returns></returns>
    public static List<Tuning> GetTunings()
    {
        return
        [
            new Tuning
            {
                Name = "Standard tuning (E)",
                Notes = new[] { "E", "A", "D", "G", "B", "E" },
                Pitches = new[] { "E2", "A2", "D3", "G3", "B3", "E4" }
            },

            new Tuning
            {
                Name = "Half-step down (E flat)",
                Notes = new[] { "D#", "G#", "C#", "F#", "A#", "D#" },
                Pitches = new[] { "D#2", "G#2", "C#3", "F#3", "A#3", "D#4" }
            },

            new Tuning
            {
                Name = "Drop D",
                Notes = new[] { "D", "A", "D", "G", "B", "E" },
                Pitches = new[] { "D2", "A2", "D3", "G3", "B3", "E4" }
            },

            new Tuning
            {
                Name = "Drop C#",
                Notes = new[] { "C#", "G#", "C#", "F#", "A#", "D#" },
                Pitches = new[] { "C#2", "G#2", "C#3", "F#3", "A#3", "D#4" }
            },

            new Tuning
            {
                Name = "D tuning",
                Notes = new[] { "D", "G", "C", "F", "A", "D" },
                Pitches = new[] { "D2", "G2", "C3", "F3", "A3", "D4" }
            },

            new Tuning
            {
                Name = "Drop C",
                Notes = new[] { "C", "G", "C", "F", "A", "D" },
                Pitches = new[] { "C2", "G2", "C3", "F3", "A3", "D4" }
            },

            new Tuning
            {
                Name = "C# tuning",
                Notes = new[] { "C#", "F#", "B", "E", "G#", "C#" },
                Pitches = new[] { "C#2", "F#2", "B2", "E3", "G#3", "C#3" }
            },

            new Tuning
            {
                Name = "Drop B",
                Notes = new[] { "B", "F#", "B", "E", "G#", "C#" },
                Pitches = new[] { "B1", "F#2", "B2", "E3", "G#3", "C#4" }
            },

            new Tuning
            {
                Name = "C Tuning",
                Notes = new[] { "C", "F", "A#", "D#", "G", "C" },
                Pitches = new[] { "C2", "F2", "A#2", "D#3", "G3", "C4" }
            },

            new Tuning
            {
                Name = "B tuning (B standard)",
                Notes = new[] { "B", "E", "A", "D", "F#", "B" },
                Pitches = new[] { "B1", "E2", "A2", "D3", "F#3", "B3" }
            },

            new Tuning
            {
                Name = "Drop A",
                Notes = new[] { "A", "E", "A", "D", "F#", "B" },
                Pitches = new[] { "A1", "E2", "A2", "D3", "F#3", "B3" }
            },

            new Tuning
            {
                Name = "Standard tuning, 7 strings",
                Notes = new[] { "B", "E", "A", "D", "G", "B", "E" },
                Pitches = new[] { "B1", "E2", "A2", "D3", "G3", "B3", "E4" }
            },

            new Tuning
            {
                Name = "Standard tuning, 8 strings",
                Notes = new[] { "G", "B", "E", "A", "D", "G", "B", "E" },
                Pitches = new[] { "G1", "B1", "E2", "A2", "D3", "G3", "B3", "E4" }
            }
        ];
    }
    /// <summary>
    ///  Finds the closest musical note for a given pitch.
    /// </summary>
    /// <param name="pitch"></param>
    /// <returns></returns>
    private Tuple<string, double> FindClosestNote(double pitch)
    {
        var i = (int)Math.Round(12 * Math.Log2(pitch / ConcertPitch));
        // Determine the MIDI note number based on A4 = 69.
        var midiNote = i + 69;
        // Calculate the octave from the MIDI note number.
        var octave = (midiNote / 12) - 1;
        // Determine note name using the note array.
        var noteName = _allNotes[(i + 1200) % 12];
        var closestNote = noteName + octave;
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

        // Skip if signal power is too low
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

        // Suppress mains hum
        const int limit = (int)(62 / DeltaFreq);
        for (var i = 0; i < limit && i < magnitudeSpec.Length; i++)
        {
            magnitudeSpec[i] = 0;
        }

        // Noise suppression based on octave bands
         for (var j = 0; j < _octaveBands.Length - 1; j++)
        {
            var indStart = (int)(_octaveBands[j] / DeltaFreq);
            var indEnd = (int)(_octaveBands[j + 1] / DeltaFreq);
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

        // Interpolate spectrum using MathNet.Numerics interpolation
        var ipolLength = magnitudeSpec.Length * NumHps;
        var xValues = Enumerable.Range(0, magnitudeSpec.Length).Select(x => (double)x).ToArray();
        var xInterp = Enumerable.Range(0, ipolLength).Select(i => i / (double)NumHps).ToArray();
        var spline = Interpolate.Linear(xValues, magnitudeSpec);
        var magSpecIpol = xInterp.Select(x => spline.Interpolate(x)).ToArray();

        // Normalize interpolated spectrum
        var normFactor = Math.Sqrt(magSpecIpol.Select(v => v * v).Sum());
        for (var i = 0; i < magSpecIpol.Length; i++)
        {
            magSpecIpol[i] /= normFactor;
        }

        // Compute Harmonic Product Spectrum (HPS)
        var hpsSpec = magSpecIpol.ToArray();

        for (var i = 0; i < NumHps; i++)
        {
            var downsampleFactor = i + 1;
            var hpsLength = (int)Math.Ceiling(magSpecIpol.Length / (double)downsampleFactor);

            var tmpHpsSpec = new double[hpsLength];

            // Multiply hpsSpec[:hpsLength] * magSpecIpol[::downsampleFactor]
            for (var j = 0; j < hpsLength; j++)
            {
                tmpHpsSpec[j] = hpsSpec[j] * magSpecIpol[j * downsampleFactor];
            }

            // Check if tmpHpsSpec has any non-zero elements
            if (tmpHpsSpec.All(value => value == 0))
            {
                break;
            }

            hpsSpec = tmpHpsSpec;
        }

        // Find the maximum in HPS spectrum
        var maxInd = Array.IndexOf(hpsSpec, hpsSpec.Max());
        var maxFreq = maxInd * (SampleFreq / (double)WindowSize) / NumHps;

        var (closestNote, closestPitch) = FindClosestNote(maxFreq);
        maxFreq = Math.Round(maxFreq, 1);
        closestPitch = Math.Round(closestPitch, 1);

        // Update note buffer
        _noteBuffer = [closestNote, _noteBuffer[0]];

        if (_noteBuffer.Any(n => n != _noteBuffer[0])) return;

        SetCurrentNoteSafe(new ClosestNote
        {
            Note = closestNote,
            MaxFrequency = maxFreq,
            ClosestPitch = closestPitch
        });
        Console.WriteLine($"Note: {closestNote}, Max Frequency: {maxFreq} Hz, Closest Pitch: {closestPitch} Hz");
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
    public string MaxFrequencyText => MaxFrequency > 0 ? $"{MaxFrequency}" : "";
    public string ClosestPitchText => ClosestPitch > 0 ? $"{ClosestPitch}" : "";
    public string FrequencyPitchText => MaxFrequency + ClosestPitch > 0 ? $"{MaxFrequency}/{ClosestPitch} Hz" : "";
}
public class Tuning
{
    public string Name { get; set; }
    public string[] Notes { get; set; }
    public string[] Pitches { get; set; }
}