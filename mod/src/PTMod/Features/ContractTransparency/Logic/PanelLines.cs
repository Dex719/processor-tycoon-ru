using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>How a line of a panel is coloured: the screens map it to the colours of the game's theme (positive, negative, warning), <see cref="Muted"/> is the dimmed body text.</summary>
    public enum LineTone
    {
        Normal = 0,
        Good = 1,
        Bad = 2,
        Warn = 3,
        Muted = 4,
    }

    /// <summary>
    /// What an argument of a <see cref="PanelLine"/> is, so the screen can print it the way the game prints such a thing (IPS and money through the game's formatters, a share with the decimals of
    /// <c>rules.json</c> and the decimal separator of the language) while the logic stays free of the game and the tests can read the numbers.
    /// </summary>
    public enum ArgKind
    {
        /// <summary>A text from the game or the log (a name); the screen cuts it of markup.</summary>
        Text = 0,

        /// <summary>An IPS value as the game stores it (<c>Cpu.Ips</c>, <c>Contract.MinIps</c>); printed by <c>StringFormatter.FloatToIps</c>.</summary>
        Ips = 1,

        /// <summary>A whole number with the grouping of thousands the game uses for units (<c>52 000</c>).</summary>
        Count = 2,

        /// <summary>A whole number, digits only (lines, days, percent points).</summary>
        Whole = 3,

        /// <summary>A number that already is a percent (<c>75</c>), printed as a whole number.</summary>
        Percent = 4,

        /// <summary>A share 0..1 printed as a percent with <c>display.percentDecimals</c> (<c>0.35</c> is <c>35</c>).</summary>
        Fraction = 5,

        /// <summary>A probability 0..1 printed as a percent with <c>display.actualPercentDecimals</c>.</summary>
        Actual = 6,

        /// <summary>Money, printed by <c>StringFormatter.FloatToMoney</c>.</summary>
        Money = 7,

        /// <summary>A factor such as <c>1.25</c> (the text puts the sign: <c>×{0}</c>).</summary>
        Multiplier = 8,

        /// <summary>A key of the string table; the screen puts its text in the current language.</summary>
        Key = 9,

        /// <summary>Several keys, comma separated; the screen translates each and joins them with ", ".</summary>
        KeyList = 10,
    }

    /// <summary>One argument of a line: what it is (<see cref="ArgKind"/>) and its value, a number or a text.</summary>
    public struct PanelArg
    {
        public ArgKind Kind;
        public double Number;
        public string Text;

        public static PanelArg OfText(string text)
        {
            return new PanelArg { Kind = ArgKind.Text, Text = text ?? string.Empty };
        }

        public static PanelArg OfKey(string key)
        {
            return new PanelArg { Kind = ArgKind.Key, Text = key ?? string.Empty };
        }

        public static PanelArg OfKeys(string commaSeparatedKeys)
        {
            return new PanelArg { Kind = ArgKind.KeyList, Text = commaSeparatedKeys ?? string.Empty };
        }

        public static PanelArg OfNumber(ArgKind kind, double number)
        {
            if (double.IsNaN(number) || double.IsInfinity(number)) number = 0;
            return new PanelArg { Kind = kind, Number = number };
        }

        public static PanelArg Ips(double value)
        {
            return OfNumber(ArgKind.Ips, value);
        }

        public static PanelArg Count(double value)
        {
            return OfNumber(ArgKind.Count, value);
        }

        public static PanelArg Whole(double value)
        {
            return OfNumber(ArgKind.Whole, value);
        }

        public static PanelArg Percent(double value)
        {
            return OfNumber(ArgKind.Percent, value);
        }

        public static PanelArg Fraction(double value)
        {
            return OfNumber(ArgKind.Fraction, value);
        }

        public static PanelArg Actual(double value)
        {
            return OfNumber(ArgKind.Actual, value);
        }

        public static PanelArg Money(double value)
        {
            return OfNumber(ArgKind.Money, value);
        }

        public static PanelArg Multiplier(double value)
        {
            return OfNumber(ArgKind.Multiplier, value);
        }
    }

    /// <summary>
    /// One line of a panel, before it is drawn: the key of the string table, its arguments and how it looks. The panels (breakdown of the chance, review screen, log window, preview of the automation)
    /// are lists of these; the logic fills the lists with the numbers and the decisions of what to show, the screens print them (<c>ModL10n.L(Key, ...)</c>) in the colours of the theme.
    /// </summary>
    public sealed class PanelLine
    {
        public PanelLine(string key, LineTone tone, params PanelArg[] args)
        {
            Key = key ?? string.Empty;
            Tone = tone;
            Args = args ?? new PanelArg[0];
        }

        /// <summary>The key of the text (<c>ct.req.ips</c>).</summary>
        public string Key { get; private set; }

        public PanelArg[] Args { get; private set; }

        public LineTone Tone { get; private set; }

        /// <summary>A heading or an important line.</summary>
        public bool Bold { get; set; }

        /// <summary>How far the line is pushed in (0 for none); a detail line under a requirement has 1.</summary>
        public int Indent { get; set; }

        /// <summary>The same line, bold.</summary>
        public PanelLine AsBold()
        {
            Bold = true;
            return this;
        }

        /// <summary>The same line, pushed in.</summary>
        public PanelLine Indented(int level)
        {
            Indent = level < 0 ? 0 : level;
            return this;
        }

        /// <summary>The arguments printed with <paramref name="formatter"/>, ready for <c>ModL10n.L(Key, ...)</c>.</summary>
        public string[] Format(IArgFormatter formatter)
        {
            var text = new string[Args.Length];
            for (int i = 0; i < Args.Length; i++) text[i] = formatter.Format(Args[i]);
            return text;
        }
    }

    /// <summary>Prints an argument of a <see cref="PanelLine"/>. The screens use the game's formatters; <see cref="PlainArgFormatter"/> is the deterministic one for tests and logs.</summary>
    public interface IArgFormatter
    {
        string Format(PanelArg arg);
    }

    /// <summary>
    /// The formatter that needs nothing from the game: numbers in the invariant culture, a key printed as <c>[key]</c>. The percent decimals and the decimal comma are parameters, like the settings of the
    /// real formatter, so a test can check what the player would see in each setting.
    /// </summary>
    public sealed class PlainArgFormatter : IArgFormatter
    {
        private readonly int _percentDecimals;
        private readonly int _actualDecimals;
        private readonly bool _decimalComma;

        public PlainArgFormatter() : this(0, 0, false)
        {
        }

        public PlainArgFormatter(int percentDecimals, int actualDecimals, bool decimalComma)
        {
            _percentDecimals = percentDecimals;
            _actualDecimals = actualDecimals;
            _decimalComma = decimalComma;
        }

        public string Format(PanelArg arg)
        {
            switch (arg.Kind)
            {
                case ArgKind.Text:
                    return arg.Text ?? string.Empty;
                case ArgKind.Ips:
                    return arg.Number.ToString("0.##", CultureInfo.InvariantCulture);
                case ArgKind.Count:
                case ArgKind.Whole:
                case ArgKind.Percent:
                    return Math.Round(arg.Number, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
                case ArgKind.Fraction:
                    return ChanceFormat.Percent(arg.Number, _percentDecimals, _decimalComma);
                case ArgKind.Actual:
                    return ChanceFormat.Percent(arg.Number, _actualDecimals, _decimalComma);
                case ArgKind.Money:
                    return arg.Number.ToString("0.##", CultureInfo.InvariantCulture);
                case ArgKind.Multiplier:
                    return arg.Number.ToString("0.##", CultureInfo.InvariantCulture);
                case ArgKind.Key:
                    return "[" + (arg.Text ?? string.Empty) + "]";
                case ArgKind.KeyList:
                    return KeyListText(arg.Text);
                default:
                    return string.Empty;
            }
        }

        private static string KeyListText(string keys)
        {
            if (string.IsNullOrEmpty(keys)) return string.Empty;
            var parts = new List<string>();
            foreach (string key in keys.Split(','))
            {
                string trimmed = key.Trim();
                if (trimmed.Length > 0) parts.Add("[" + trimmed + "]");
            }
            return string.Join(", ", parts.ToArray());
        }
    }
}
