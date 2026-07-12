namespace ToperJarvis.Speech.Endpointing;

/// <summary>
/// Ekstrakcja log-mel spektrogramu zgodna z <c>WhisperFeatureExtractor</c> z HF
/// <c>transformers</c> (skala Slaney), której oczekuje graf ONNX Smart Turn v3.1.
///
/// Odtwarza dokładnie pipeline z <c>_np_extract_fbank_features</c> +
/// <c>audio_utils.spectrogram</c>:
/// (a) zero-mean/unit-variance normalizacja surowego PCM po całym buforze,
/// (b) centered STFT (Hann periodic n_fft=400, hop=160, reflect pad n_fft/2, power=2.0),
///     801 ramek → odrzucenie ostatniej → 800,
/// (c) filtrbank mel Slaney (80 filtrów, 201 binów, 0–8000 Hz, norm="slaney"),
/// (d) log10, clip do (max-8.0), przeskalowanie (x+4)/4.
///
/// Zwraca spłaszczony wektor [nMels * frames] (mel-bin major, tj. mel[m,f] pod
/// indeksem m*frames + f), pasujący do tensora [80, 800] grafu ONNX.
///
/// Obliczenia prowadzone w double dla zgodności numerycznej z numpy (który
/// wewnętrznie używa float64). Nie jest to jeszcze hot-path — priorytetem jest
/// poprawność walidowana złotym wektorem referencyjnym.
/// </summary>
public static class WhisperMelSpectrogram
{
    private const double WaveNormEps = 1e-7;   // (x-mean)/sqrt(var+eps)
    private const double MelFloor = 1e-10;      // maximum(mel_floor, mel)
    private const double DynamicRangeDb = 8.0;  // log_spec.max() - 8.0

    /// <summary>
    /// Liczy log-mel dla bufora PCM 16 kHz mono. Dla dokładnie 128000 próbek
    /// (8 s) zwraca 80*800 wartości. Padding/truncation do 8 s należy do Task 1.5.
    /// </summary>
    public static float[] Compute(ReadOnlySpan<float> pcm16k, int nMels = 80, int nFft = 400, int hop = 160)
    {
        var n = pcm16k.Length;

        // (a) Zero-mean/unit-variance normalizacja po całym buforze (float64).
        double mean = 0.0;
        for (var i = 0; i < n; i++)
            mean += pcm16k[i];
        mean /= n;

        double var = 0.0;
        for (var i = 0; i < n; i++)
        {
            var d = pcm16k[i] - mean;
            var += d * d;
        }
        var /= n; // populacyjna wariancja (numpy ddof=0)

        var invStd = 1.0 / Math.Sqrt(var + WaveNormEps);
        var normed = new double[n];
        for (var i = 0; i < n; i++)
            normed[i] = (pcm16k[i] - mean) * invStd;

        // (b) Centered STFT: reflect-pad o nFft/2 z obu stron.
        var pad = nFft / 2;
        var padded = ReflectPad(normed, pad);

        // Liczba ramek: 1 + floor((len - nFft)/hop). Ostatnią odrzucamy → frames.
        var numFramesFull = 1 + (padded.Length - nFft) / hop;
        var frames = numFramesFull - 1; // odrzucenie ostatniej ramki (log_spec[:, :-1])

        var window = HannPeriodic(nFft);
        var numFreqBins = nFft / 2 + 1;

        // Prekomputacja tabel DFT (rfft nFft-punktowy → numFreqBins binów).
        // twiddleCos[k*nFft + t] = cos(2*pi*k*t/nFft), twiddleSin analogicznie.
        var twiddleCos = new double[numFreqBins * nFft];
        var twiddleSin = new double[numFreqBins * nFft];
        for (var k = 0; k < numFreqBins; k++)
        {
            for (var t = 0; t < nFft; t++)
            {
                var ang = 2.0 * Math.PI * k * t / nFft;
                twiddleCos[k * nFft + t] = Math.Cos(ang);
                twiddleSin[k * nFft + t] = Math.Sin(ang);
            }
        }

        // (c) Filtrbank mel Slaney: [numFreqBins x nMels].
        var melFilters = SlaneyMelFilterBank(numFreqBins, nMels, 0.0, 8000.0, 16000);

        // Bufor okienkowanej ramki oraz mocy widma.
        var buffer = new double[nFft];
        var power = new double[numFreqBins];

        // log_spec[m, f] w układzie mel-bin major.
        var logMel = new double[nMels * frames];
        var maxLog = double.NegativeInfinity;

        for (var f = 0; f < frames; f++)
        {
            var start = f * hop;
            for (var t = 0; t < nFft; t++)
                buffer[t] = padded[start + t] * window[t];

            // rfft + |.|^2
            for (var k = 0; k < numFreqBins; k++)
            {
                double re = 0.0, im = 0.0;
                var baseIdx = k * nFft;
                for (var t = 0; t < nFft; t++)
                {
                    var s = buffer[t];
                    re += s * twiddleCos[baseIdx + t];
                    im -= s * twiddleSin[baseIdx + t];
                }
                power[k] = re * re + im * im;
            }

            // mel = maximum(mel_floor, filters^T @ power), potem log10
            for (var m = 0; m < nMels; m++)
            {
                double acc = 0.0;
                for (var k = 0; k < numFreqBins; k++)
                    acc += melFilters[k * nMels + m] * power[k];

                if (acc < MelFloor)
                    acc = MelFloor;

                var lv = Math.Log10(acc);
                logMel[m * frames + f] = lv;
                if (lv > maxLog)
                    maxLog = lv;
            }
        }

        // (d) clip do (max-8.0) i przeskalowanie (x+4)/4.
        var floor = maxLog - DynamicRangeDb;
        var result = new float[nMels * frames];
        for (var i = 0; i < logMel.Length; i++)
        {
            var v = logMel[i];
            if (v < floor)
                v = floor;
            result[i] = (float)((v + 4.0) / 4.0);
        }

        return result;
    }

    /// <summary>Periodic Hann długości <paramref name="length"/> (np.hanning(length+1)[:-1]).</summary>
    private static double[] HannPeriodic(int length)
    {
        var w = new double[length];
        for (var n = 0; n < length; n++)
            w[n] = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * n / length);
        return w;
    }

    /// <summary>Reflect padding zgodne z numpy (bez powtarzania próbki brzegowej).</summary>
    private static double[] ReflectPad(double[] x, int pad)
    {
        var n = x.Length;
        var outp = new double[n + 2 * pad];
        for (var i = 0; i < n; i++)
            outp[pad + i] = x[i];

        // Lewa strona: outp[pad-1-j] = x[1+j] dla j=0..pad-1  (mirror bez brzegu).
        for (var j = 0; j < pad; j++)
            outp[pad - 1 - j] = x[j + 1];

        // Prawa strona: outp[pad+n+j] = x[n-2-j].
        for (var j = 0; j < pad; j++)
            outp[pad + n + j] = x[n - 2 - j];

        return outp;
    }

    /// <summary>
    /// Filtrbank mel Slaney [numFreqBins x nMels] (mel-bin major: [k*nMels + m]),
    /// odpowiednik <c>mel_filter_bank(norm="slaney", mel_scale="slaney")</c> /
    /// <c>librosa.mel(norm='slaney', htk=False)</c>.
    /// </summary>
    private static double[] SlaneyMelFilterBank(int numFreqBins, int nMels, double fMin, double fMax, int sampleRate)
    {
        var melMin = HertzToMelSlaney(fMin);
        var melMax = HertzToMelSlaney(fMax);

        // mel_freqs = linspace(melMin, melMax, nMels+2); filter_freqs = mel_to_hertz(...)
        var count = nMels + 2;
        var filterFreqs = new double[count];
        for (var i = 0; i < count; i++)
        {
            var mel = melMin + (melMax - melMin) * i / (count - 1);
            filterFreqs[i] = MelToHertzSlaney(mel);
        }

        // fft_freqs = linspace(0, sampleRate/2, numFreqBins)
        var fftFreqs = new double[numFreqBins];
        var nyquist = sampleRate / 2;
        for (var k = 0; k < numFreqBins; k++)
            fftFreqs[k] = (double)nyquist * k / (numFreqBins - 1);

        // filter_diff = diff(filter_freqs)
        var filterDiff = new double[count - 1];
        for (var i = 0; i < count - 1; i++)
            filterDiff[i] = filterFreqs[i + 1] - filterFreqs[i];

        var filters = new double[numFreqBins * nMels];
        for (var k = 0; k < numFreqBins; k++)
        {
            for (var m = 0; m < nMels; m++)
            {
                // slopes[k][j] = filter_freqs[j] - fft_freqs[k]
                var downSlope = -(filterFreqs[m] - fftFreqs[k]) / filterDiff[m];
                var upSlope = (filterFreqs[m + 2] - fftFreqs[k]) / filterDiff[m + 1];
                var val = Math.Min(downSlope, upSlope);
                if (val < 0.0)
                    val = 0.0;
                filters[k * nMels + m] = val;
            }
        }

        // Slaney norm: enorm[m] = 2 / (filter_freqs[m+2] - filter_freqs[m])
        for (var m = 0; m < nMels; m++)
        {
            var enorm = 2.0 / (filterFreqs[m + 2] - filterFreqs[m]);
            for (var k = 0; k < numFreqBins; k++)
                filters[k * nMels + m] *= enorm;
        }

        return filters;
    }

    // Skala Slaney (mel_scale="slaney") — patrz transformers.audio_utils.hertz_to_mel.
    private const double SlaneyMinLogHertz = 1000.0;
    private const double SlaneyMinLogMel = 15.0;

    private static double HertzToMelSlaney(double freq)
    {
        var logstep = 27.0 / Math.Log(6.4);
        if (freq >= SlaneyMinLogHertz)
            return SlaneyMinLogMel + Math.Log(freq / SlaneyMinLogHertz) * logstep;
        return 3.0 * freq / 200.0;
    }

    private static double MelToHertzSlaney(double mel)
    {
        var logstep = Math.Log(6.4) / 27.0;
        if (mel >= SlaneyMinLogMel)
            return SlaneyMinLogHertz * Math.Exp(logstep * (mel - SlaneyMinLogMel));
        return 200.0 * mel / 3.0;
    }
}
