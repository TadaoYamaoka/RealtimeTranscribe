using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Linq;

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

using NAudio.Wave;
using NAudio.Wave.SampleProviders;

using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Single;

namespace RealtimeTranscribe;
class Whisper
{
    static int[] SUPRESS_TOKENS = new int[] { 1, 2, 7, 8, 9, 10, 14, 25, 26, 27, 28, 29, 31, 58, 59, 60, 61, 62, 63, 90, 91, 92, 93, 359, 503, 522, 542, 873, 893, 902, 918, 922, 931, 1350, 1853, 1982, 2460, 2627, 3246, 3253, 3268, 3536, 3846, 3961, 4183, 4667, 6585, 6647, 7273, 9061, 9383, 10428, 10929, 11938, 12033, 12331, 12562, 13793, 14157, 14635, 15265, 15618, 16553, 16604, 18362, 18956, 20075, 21675, 22520, 26130, 26161, 26435, 28279, 29464, 31650, 32302, 32470, 36865, 42863, 47425, 49870, 50254, 50258, 50360, 50361, 50362 };
    const int MAX_INITIAL_TIMESTAMP_INDEX = 50;
    const int N_TEXT_CTX = 448;

    static byte[] StreamToBytes(Stream stream)
    {
        using (MemoryStream ms = new MemoryStream())
        {
            stream.CopyTo(ms);
            return ms.ToArray();
        }
    }

    static float[] LogSoftmax(Span<float> tensor)
    {
        float sumExp = 0;
        foreach (var v in tensor)
            sumExp += MathF.Exp(v);
        var logSumExp = MathF.Log(sumExp);
        var results = new float[tensor.Length];
        for (int i = 0; i < tensor.Length; i++)
            results[i] = tensor[i] - logSumExp;
        return results;
    }
    static float[] Softmax(Span<float> tensor)
    {
        float max = 0;
        foreach (var v in tensor)
            if (v > max)
                max = v;
        float sumExp = 0;
        foreach (var v in tensor)
            sumExp += MathF.Exp(v - max);
        var results = new float[tensor.Length];
        for (int i = 0; i < tensor.Length; i++)
            results[i] = tensor[i] / sumExp;
        return results;
    }

    const int SAMPLE_RATE = 16000;
    const int N_FFT = 400;
    const int N_MELS = 80;
    const int HOP_LENGTH = 160;
    const int CHUNK_LENGTH = 30;
    const int N_SAMPLES = CHUNK_LENGTH * SAMPLE_RATE;
    const int N_FRAMES = N_SAMPLES / HOP_LENGTH;

    private static Matrix<float> matrixMelFilters;
    private static float[] window;

    static Whisper() {
        // mel_filters
        var byteMelFilters = StreamToBytes(Assembly.GetExecutingAssembly().GetManifestResourceStream("RealtimeTranscribe.mel_filters.bin"));
        var melFilters = MemoryMarshal.Cast<byte, float>(byteMelFilters).ToArray();
        matrixMelFilters = Matrix.Build.Dense((N_FFT / 2 + 1), N_MELS, melFilters);

        window = Window.Hann(N_FFT).Select(x => (float)x).ToArray();
    }

    private InferenceSession? encoderSession;
    private InferenceSession? decoderSession;

    public Whisper()
    {
        var options = new SessionOptions();
        //options.AppendExecutionProvider_DML();
        encoderSession = new InferenceSession(StreamToBytes(Assembly.GetExecutingAssembly().GetManifestResourceStream("RealtimeTranscribe.encoder.onnx")), options);
        decoderSession = new InferenceSession(StreamToBytes(Assembly.GetExecutingAssembly().GetManifestResourceStream("RealtimeTranscribe.decoder.onnx")), options);
    }

    public void Dispose()
    {
        if (encoderSession != null) encoderSession.Dispose();
        if (decoderSession != null) decoderSession.Dispose();
    }

    private float[]? audio_prev = null;

    public (string?, string?) decode(ISampleProvider provider)
    {
        // load_audio and pad_or_trim
        var audio = new float[N_SAMPLES];
        int audio_offset = 0;
        if (audio_prev != null)
        {
            audio_offset = audio_prev.Length; 
            audio_prev.CopyTo(audio, 0);
            audio_prev = null;
        }
        var resampler = new WdlResamplingSampleProvider(provider, SAMPLE_RATE).ToMono();
        var len = resampler.Read(audio, audio_offset, audio.Length) + audio_offset;

        if (len == 0 || audio[0..len].Max(x => x * x) < 0.001)
            return (null, null);

        // find silent periods
        float minVol = Single.MaxValue;
        int minIndex = len * 4 / 5;
        int len2 = len;
        if (minIndex > 100)
        {
            for (int i = minIndex; i < len - 50; i++)
            {
                var vol = audio[(i - 50)..(i + 50)].Average(x => x * x);
                if (vol < minVol)
                {
                    minVol = vol;
                    minIndex = i;
                }
            }
            len2 = minIndex + 50;
            audio_prev = audio[len2..len];
        }

        // log_mel_spectrogram
        var magnitudes = new float[N_FRAMES, N_FFT / 2 + 1];
        for (int i = 0, n = 0; i < len2; i += HOP_LENGTH, n++)
        {
            var frame = new Complex32[N_FFT];
            for (int j = 0; j < N_FFT; j++)
            {
                var p = i + j - N_FFT / 2;
                //  padding reflect
                if (p < 0)
                {
                    p *= -1;
                }
                else if (p >= N_SAMPLES)
                {
                    p = N_SAMPLES - (p - N_SAMPLES) - 1;
                }
                frame[j] = audio[p] * window[j];
            }

            Fourier.Forward(frame, FourierOptions.NoScaling);

            for (int j = 0; j < N_FFT / 2 + 1; j++)
            {
                magnitudes[n, j] = MathF.Pow(frame[j].Magnitude, 2);
            }
        }
        var logSpec = Matrix.Build.DenseOfArray(magnitudes) * matrixMelFilters;
        logSpec = logSpec.PointwiseMaximum(1e-10F).PointwiseLog10();
        logSpec = logSpec.PointwiseMaximum(logSpec.Enumerate().Max() - 8.0F);
        logSpec = (logSpec + 4.0F) / 4.0F;
        var mel = logSpec.Enumerate().ToArray();

        var inputs = new List<NamedOnnxValue>() {
            NamedOnnxValue.CreateFromTensor<float>("mel",
                new DenseTensor<float>(
                    mel,
                    new int[] { 1, N_MELS, N_FRAMES })),
        };
        var outputs = encoderSession.Run(inputs).ToList();
        var n_layer_cross_k = outputs[0];
        var n_layer_cross_v = outputs[1];

        // detect_language
        var tokens = new long[] { Tokenizer.SOT };
        var n_layer_self_k_cache = NamedOnnxValue.CreateFromTensor<float>(
            "in_n_layer_self_k_cache",
            new DenseTensor<float>(new int[] { 6, 1, N_TEXT_CTX, 512 }));
        var n_layer_self_v_cache = NamedOnnxValue.CreateFromTensor<float>(
            "in_n_layer_self_v_cache",
            new DenseTensor<float>(new int[] { 6, 1, N_TEXT_CTX, 512 }));
        var offset = new DenseTensor<long>(new long[] { 0 }, new int[] { 1 });
        inputs = new List<NamedOnnxValue>() {
            NamedOnnxValue.CreateFromTensor<long>("tokens",
                new DenseTensor<long>(
                    tokens,
                    new int[] { 1, tokens.Length })),
            n_layer_self_k_cache,
            n_layer_self_v_cache,
            n_layer_cross_k,
            n_layer_cross_v,
            NamedOnnxValue.CreateFromTensor<long>("offset", offset),
        };
        outputs = decoderSession.Run(inputs).ToList();
        var logits = outputs[0];

        // collect detected languages; suppress all non-language tokens
        var logitsTensor = logits.AsTensor<float>() as DenseTensor<float>;
        float maxLogits = Single.MinValue;
        int languageToken = 0;
        for (int i = 0; i < logitsTensor.Dimensions[2]; i++)
        {
            if (Tokenizer.ALL_LANGUAGE_TOKENS.ContainsKey(i))
            {
                if (logitsTensor[0, 0, i] > maxLogits)
                {
                    maxLogits = logitsTensor[0, 0, i];
                    languageToken = i;
                }
            }
        }
        var language = Tokenizer.ALL_LANGUAGE_TOKENS[languageToken];

        // main_loop
        tokens = new long[] { Tokenizer.SOT, languageToken, Tokenizer.TRANSCRIBE };
        const int INITIAL_TOKEN_LENGTH = 3;
        float sumLogprob = 0;
        for (int i = 0; i < 224; i++)
        {
            long[] inTokens;
            if (tokens.Length > INITIAL_TOKEN_LENGTH)
            {
                // only need to use the last token except in the first forward pass
                offset[0] = tokens.Length - 1;
                inTokens = new long[] { tokens.Last() };
            }
            else
            {
                inTokens = tokens;
            }
            n_layer_self_k_cache.Name = "in_n_layer_self_k_cache";
            n_layer_self_v_cache.Name = "in_n_layer_self_v_cache";
            inputs = new List<NamedOnnxValue>() {
                NamedOnnxValue.CreateFromTensor<long>("tokens",
                    new DenseTensor<long>(
                        inTokens,
                        new int[] { 1, inTokens.Length })),
                n_layer_self_k_cache,
                n_layer_self_v_cache,
                n_layer_cross_k,
                n_layer_cross_v,
                NamedOnnxValue.CreateFromTensor<long>("offset", offset),
            };
            outputs = decoderSession.Run(inputs).ToList();
            logits = outputs[0];
            n_layer_self_k_cache = outputs[1];
            n_layer_self_v_cache = outputs[2];

            logitsTensor = logits.AsTensor<float>() as DenseTensor<float>;
            var logitsBuffer = logitsTensor.Buffer.Span.Slice(logitsTensor.Dimensions[2] * (logitsTensor.Dimensions[1] - 1));

            // save no_speech_probs
            if (i == 0)
            {
                var probsAtSot = Softmax(logitsTensor.Buffer.Span.Slice(0, logitsTensor.Dimensions[2]));
            }

            // apply the logit filters, e.g. for suppressing or applying penalty to
            // SuppressBlank
            if (i == 0)
            {
                logitsBuffer[220/*encode(" ")*/] = logitsBuffer[Tokenizer.EOT] = Single.MinValue;
            }
            // SuppressTokens
            foreach (var suppressToken in SUPRESS_TOKENS)
            {
                logitsBuffer[suppressToken] = Single.MinValue;
            }
            // ApplyTimestampRules
            // suppress <|notimestamps|> which is handled by without_timestamps
            logitsBuffer[Tokenizer.NO_TIMESTAMPS] = Single.MinValue;
            // timestamps have to appear in pairs, except directly before EOT; mask logits accordingly
            var seq = tokens.AsSpan().Slice(3);
            var lastWasTimestamp = seq.Length >= 1 && seq[seq.Length - 1] >= Tokenizer.TIMESTAMP_BEGIN;
            var penultimateWasTimestamp = seq.Length < 2 || seq[seq.Length - 2] >= Tokenizer.TIMESTAMP_BEGIN;
            if (lastWasTimestamp)
            {
                if (penultimateWasTimestamp) // has to be non-timestamp
                {
                    for (int j = Tokenizer.TIMESTAMP_BEGIN; j < logitsBuffer.Length; j++)
                        logitsBuffer[j] = Single.MinValue;
                }
                else
                {
                    for (int j = 0; j < Tokenizer.EOT; j++)
                        logitsBuffer[j] = Single.MinValue;
                }
            }
            if (i == 0)
            {
                // suppress generating non-timestamp tokens at the beginning
                for (int j = 0; j < Tokenizer.TIMESTAMP_BEGIN; j++)
                    logitsBuffer[j] = Single.MinValue;
                // apply the `max_initial_timestamp` option
                var lastAllowed = Tokenizer.TIMESTAMP_BEGIN + MAX_INITIAL_TIMESTAMP_INDEX;
                for (int j = lastAllowed + 1; j < logitsBuffer.Length; j++)
                    logitsBuffer[j] = Single.MinValue;
            }
            // if sum of probability over timestamps is above any other token, sample timestamp
            var logprobs = LogSoftmax(logitsBuffer);
            var timestampLogprob = Math.Log(logprobs[Tokenizer.TIMESTAMP_BEGIN..].Select(v => Math.Exp(v)).Sum());
            var maxTextTokenLogprob = logprobs[..Tokenizer.TIMESTAMP_BEGIN].Max();
            if (timestampLogprob > maxTextTokenLogprob)
            {
                for (int j = 0; j < Tokenizer.TIMESTAMP_BEGIN; j++)
                    logitsBuffer[j] = Single.MinValue;
            }

            // expand the tokens tensor with the selected next tokens
            var maxlogits = Single.MinValue;
            int nextToken = 0;
            for (int j = 0; j < logitsBuffer.Length; j++)
            {
                if (logitsBuffer[j] > maxlogits)
                {
                    maxlogits = logitsBuffer[j];
                    nextToken = j;
                }
            }
            logprobs = LogSoftmax(logitsBuffer);
            var currentLogprob = logprobs[nextToken];
            var completed = false;
            if (tokens[tokens.Length - 1] != Tokenizer.EOT)
            {
                sumLogprob += currentLogprob;
            }
            else
            {
                nextToken = Tokenizer.EOT;
                completed = true;
            }
            tokens = tokens.Append(nextToken).ToArray();

            if (completed || tokens.Length > N_TEXT_CTX)
                break;
        }

        // get the final candidates for each group, and slice between the first sampled token and EOT
        int eot = 0;
        for (; eot < tokens.Length; eot++)
        {
            if (tokens[eot] == Tokenizer.EOT)
                break;
        }
        tokens = tokens[3..eot];

        var result = Tokenizer.decode(tokens).Trim();

        return (language, result);
    }
}
