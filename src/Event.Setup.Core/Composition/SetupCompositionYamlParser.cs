namespace ISLAMU.Event.Setup.Core.Composition;

using System.Globalization;
using System.Text;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

internal static class SetupCompositionYamlParser
{
    internal static CompositionMap Parse(
        ReadOnlyMemory<byte> bytes, SetupCompositionLimits limits, CancellationToken cancellationToken,
        CompositionBudget? sharedBudget = null)
    {
        if (bytes.IsEmpty)
            throw new SetupCompositionException(SetupCompositionFailureCode.InvalidDocument);
        if (bytes.Length > limits.AggregateSourceBytes)
            throw new SetupCompositionException(SetupCompositionFailureCode.LimitExceeded);

        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes.Span);
        }
        catch (DecoderFallbackException)
        {
            throw new SetupCompositionException(SetupCompositionFailureCode.InvalidDocument);
        }

        try
        {
            if (ContainsDirectiveLine(text))
                throw new SetupCompositionException(SetupCompositionFailureCode.UnsupportedYamlGrammar);

            var reader = new EventReader(new Parser(new StringReader(text)), limits, cancellationToken);
            reader.Advance();
            reader.Require<StreamStart>();
            reader.Advance();
            DocumentStart document = reader.Require<DocumentStart>();
            if (document.Version is not null || !HasOnlyStandardTagDirectives(document))
                throw new SetupCompositionException(SetupCompositionFailureCode.UnsupportedYamlGrammar);
            reader.Advance();

            var budget = sharedBudget ?? new CompositionBudget(limits);
            CompositionNode root = ReadNode(reader, budget, limits, 1, cancellationToken);
            if (root is not CompositionMap map)
                throw new SetupCompositionException(SetupCompositionFailureCode.InvalidDocument);
            reader.Require<DocumentEnd>();
            reader.Advance();
            reader.Require<StreamEnd>();
            if (reader.Advance())
                throw new SetupCompositionException(SetupCompositionFailureCode.InvalidDocument);
            return map;
        }
        catch (SetupCompositionException) { throw; }
        catch (YamlException)
        {
            throw new SetupCompositionException(SetupCompositionFailureCode.InvalidDocument);
        }
    }

    private static bool ContainsDirectiveLine(string text)
    {
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] == '%' && (index == 0 || text[index - 1] == '\n'
                    || index == 1 && text[0] == '\uFEFF'))
                return true;
        }
        return false;
    }

    private static bool HasOnlyStandardTagDirectives(DocumentStart document)
    {
        if (document.Tags is null || document.Tags.Count != 2)
            return false;
        return document.Tags.Any(static tag => tag.Handle == "!" && tag.Prefix == "!")
            && document.Tags.Any(static tag => tag.Handle == "!!" && tag.Prefix == "tag:yaml.org,2002:");
    }

    private static CompositionNode ReadNode(
        EventReader reader, CompositionBudget budget, SetupCompositionLimits limits,
        int depth, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        budget.Node(depth);

        if (reader.Current is AnchorAlias)
            throw new SetupCompositionException(SetupCompositionFailureCode.UnsupportedYamlGrammar);
        if (reader.Current is NodeEvent nodeEvent
            && (!nodeEvent.Anchor.IsEmpty || !nodeEvent.Tag.IsEmpty))
            throw new SetupCompositionException(SetupCompositionFailureCode.UnsupportedYamlGrammar);

        if (reader.Current is MappingStart)
        {
            reader.Advance();
            var entries = new SortedDictionary<string, CompositionNode>(StringComparer.Ordinal);
            var identities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int count = 0;
            while (reader.Current is not MappingEnd)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (checked(++count) > limits.MappingEntries)
                    throw new SetupCompositionException(SetupCompositionFailureCode.LimitExceeded);
                if (reader.Current is not Scalar keyScalar)
                    throw new SetupCompositionException(SetupCompositionFailureCode.InvalidKey);
                RejectNodeAuthority(keyScalar);
                string key = keyScalar.Value;
                if (string.IsNullOrEmpty(key) || key == "null" || key == "<<")
                    throw new SetupCompositionException(key == "<<"
                        ? SetupCompositionFailureCode.UnsupportedYamlGrammar
                        : SetupCompositionFailureCode.InvalidKey);
                budget.Scalar(key);
                reader.Advance();
                CompositionNode value = ReadNode(reader, budget, limits, checked(depth + 1), cancellationToken);
                SetupCompositionNormalizer.AddKey(entries, identities, key, value);
            }
            reader.Advance();
            return new CompositionMap(entries);
        }

        if (reader.Current is SequenceStart)
        {
            reader.Advance();
            var entries = new List<CompositionNode>();
            while (reader.Current is not SequenceEnd)
            {
                if (checked(entries.Count + 1) > limits.SequenceEntries)
                    throw new SetupCompositionException(SetupCompositionFailureCode.LimitExceeded);
                entries.Add(ReadNode(reader, budget, limits, checked(depth + 1), cancellationToken));
            }
            reader.Advance();
            return new CompositionSequence(entries.AsReadOnly());
        }

        if (reader.Current is Scalar scalar)
        {
            RejectNodeAuthority(scalar);
            CompositionScalar result = ConvertScalar(scalar, budget);
            reader.Advance();
            return result;
        }

        throw new SetupCompositionException(SetupCompositionFailureCode.UnsupportedYamlGrammar);
    }

    private static void RejectNodeAuthority(NodeEvent node)
    {
        if (!node.Anchor.IsEmpty || !node.Tag.IsEmpty)
            throw new SetupCompositionException(SetupCompositionFailureCode.UnsupportedYamlGrammar);
    }

    private static CompositionScalar ConvertScalar(Scalar scalar, CompositionBudget budget)
    {
        string value = scalar.Value;
        budget.Scalar(value);
        if (scalar.Style != ScalarStyle.Plain)
            return new CompositionScalar(CompositionScalarKind.String, value);

        if (value == "true" || value == "false")
            return new CompositionScalar(CompositionScalarKind.Boolean, value);
        if (value == "null")
            return new CompositionScalar(CompositionScalarKind.Null, null);
        if (IsCanonicalInteger(value))
            return new CompositionScalar(CompositionScalarKind.Integer, value);
        if (IsAmbiguousPlainScalar(value))
            throw new SetupCompositionException(SetupCompositionFailureCode.InvalidScalar);
        return new CompositionScalar(CompositionScalarKind.String, value);
    }

    private static bool IsCanonicalInteger(string value)
    {
        if (value == "0")
            return true;
        int index = value.Length > 0 && value[0] == '-' ? 1 : 0;
        if (index == 1 && (value.Length == 1 || value[index] == '0'))
            return false;
        if (index >= value.Length || value[index] is < '1' or > '9')
            return false;
        for (; index < value.Length; index++)
        {
            if (value[index] is < '0' or > '9')
                return false;
        }
        return true;
    }

    private static bool IsAmbiguousPlainScalar(string value)
    {
        string folded = value.ToLowerInvariant();
        if (folded is "true" or "false" or "null" or "yes" or "no" or "on" or "off"
            or "~" or ".nan" or ".inf" or "+.inf" or "-.inf")
            return true;
        if (value.Length == 0 || value[0] is not ('+' or '-' or '.' or >= '0' and <= '9'))
            return false;
        return value.All(static character =>
            char.IsDigit(character) || character is '+' or '-' or '.' or ',' or '_' or ':'
                or 'e' or 'E' or 'x' or 'X' or 'o' or 'O');
    }

    private sealed class EventReader
    {
        private readonly IParser _parser;
        private readonly SetupCompositionLimits _limits;
        private readonly CancellationToken _cancellationToken;
        private int _events;

        internal EventReader(IParser parser, SetupCompositionLimits limits, CancellationToken cancellationToken)
        {
            _parser = parser;
            _limits = limits;
            _cancellationToken = cancellationToken;
        }

        internal ParsingEvent? Current => _parser.Current;

        internal bool Advance()
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (!_parser.MoveNext())
                return false;
            try { _events = checked(_events + 1); }
            catch (OverflowException) { throw new SetupCompositionException(SetupCompositionFailureCode.LimitExceeded); }
            if (_events > _limits.ParserEvents)
                throw new SetupCompositionException(SetupCompositionFailureCode.LimitExceeded);
            return true;
        }

        internal T Require<T>() where T : ParsingEvent =>
            Current as T ?? throw new SetupCompositionException(SetupCompositionFailureCode.InvalidDocument);
    }
}
