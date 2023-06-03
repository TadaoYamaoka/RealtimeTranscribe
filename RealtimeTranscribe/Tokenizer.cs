using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Reflection;
using System.Linq;
using System.IO;

namespace RealtimeTranscribe;

public class Tokenizer
{
    public const int SOT = 50258;
    public const int TRANSCRIBE = 50359;
    public const int EOT = 50257;
    public const int NO_TIMESTAMPS = 50363;
    public const int TIMESTAMP_BEGIN = 50364;

    private static Dictionary<char, byte> CHAR_BYTES;
    private static Dictionary<long, string> vocab_r;

    static Tokenizer()
    {
        var bs = new List<byte>();
        for (byte b = Convert.ToByte('!'); b <= Convert.ToByte('~'); b++)
            bs.Add(b);
        for (byte b = 0xA1; b <= 0xAC; b++)
            bs.Add(b);
        for (byte b = 0xAE; ; b++)
        {
            bs.Add(b);
            if (b == 0xFF) break;
        }

        var cs = bs.Select(i => (uint)i).ToList();
        uint n = 0;

        for (byte b = 0; b < 255u; b++)
        {
            if (!bs.Contains(b))
            {
                bs.Add(b);
                cs.Add((1u << 8) + n);
                n++;
            }
        }

        CHAR_BYTES = bs.Zip(cs, (f, t) => new { k=(char)t, v=f}).ToDictionary(kv => kv.k, kv => kv.v);

        var vocab = JsonSerializer.Deserialize<Dictionary<string, long>>(new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("RealtimeTranscribe.vocab.json")).ReadToEnd());
        vocab_r = vocab.Select(kv => new { k=kv.Value, v=kv.Key }).ToDictionary(kv => kv.k, kv => kv.v);
    }

    public static string decode(long[] tokens)
    {
        var utf8bytes = new List<byte>();
        foreach (var token in tokens)
        {
            if (vocab_r.TryGetValue(token, out var chars))
            {
                foreach (var c in chars)
                {
                    utf8bytes.Add(CHAR_BYTES[c]);
                }
            }
        }
        return System.Text.Encoding.UTF8.GetString(utf8bytes.ToArray());
    }

    public static Dictionary<long, string> ALL_LANGUAGE_TOKENS = new Dictionary<long, string> {
        { 50259, "en" },
        { 50260, "zh" },
        { 50261, "de" },
        { 50262, "es" },
        { 50263, "ru" },
        { 50264, "ko" },
        { 50265, "fr" },
        { 50266, "ja" },
        { 50267, "pt" },
        { 50268, "tr" },
        { 50269, "pl" },
        { 50270, "ca" },
        { 50271, "nl" },
        { 50272, "ar" },
        { 50273, "sv" },
        { 50274, "it" },
        { 50275, "id" },
        { 50276, "hi" },
        { 50277, "fi" },
        { 50278, "vi" },
        { 50279, "he" },
        { 50280, "uk" },
        { 50281, "el" },
        { 50282, "ms" },
        { 50283, "cs" },
        { 50284, "ro" },
        { 50285, "da" },
        { 50286, "hu" },
        { 50287, "ta" },
        { 50288, "no" },
        { 50289, "th" },
        { 50290, "ur" },
        { 50291, "hr" },
        { 50292, "bg" },
        { 50293, "lt" },
        { 50294, "la" },
        { 50295, "mi" },
        { 50296, "ml" },
        { 50297, "cy" },
        { 50298, "sk" },
        { 50299, "te" },
        { 50300, "fa" },
        { 50301, "lv" },
        { 50302, "bn" },
        { 50303, "sr" },
        { 50304, "az" },
        { 50305, "sl" },
        { 50306, "kn" },
        { 50307, "et" },
        { 50308, "mk" },
        { 50309, "br" },
        { 50310, "eu" },
        { 50311, "is" },
        { 50312, "hy" },
        { 50313, "ne" },
        { 50314, "mn" },
        { 50315, "bs" },
        { 50316, "kk" },
        { 50317, "sq" },
        { 50318, "sw" },
        { 50319, "gl" },
        { 50320, "mr" },
        { 50321, "pa" },
        { 50322, "si" },
        { 50323, "km" },
        { 50324, "sn" },
        { 50325, "yo" },
        { 50326, "so" },
        { 50327, "af" },
        { 50328, "oc" },
        { 50329, "ka" },
        { 50330, "be" },
        { 50331, "tg" },
        { 50332, "sd" },
        { 50333, "gu" },
        { 50334, "am" },
        { 50335, "yi" },
        { 50336, "lo" },
        { 50337, "uz" },
        { 50338, "fo" },
        { 50339, "ht" },
        { 50340, "ps" },
        { 50341, "tk" },
        { 50342, "nn" },
        { 50343, "mt" },
        { 50344, "sa" },
        { 50345, "lb" },
        { 50346, "my" },
        { 50347, "bo" },
        { 50348, "tl" },
        { 50349, "mg" },
        { 50350, "as" },
        { 50351, "tt" },
        { 50352, "haw" },
        { 50353, "ln" },
        { 50354, "ha" },
        { 50355, "ba" },
        { 50356, "jw" },
        { 50357, "su" },
    };


}