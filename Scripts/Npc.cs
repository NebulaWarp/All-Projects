using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A branch point in legacy Inspector-authored NPC dialogue (two-way choice).
/// Kept for backward compatibility — new dialogues should use the text-file
/// format parsed by DialogueParser, which supports any number of options.
/// </summary>
[System.Serializable]
public class DialogueChoice
{
    [Tooltip("This choice appears AFTER the player advances past this 0-based line in dialogueLines.")]
    public int afterLineIndex;
    [TextArea(1, 2)] public string optionA = "Yes";
    [TextArea(1, 2)] public string optionB = "No";
    [Tooltip("NPC lines spoken if the player picks Option A. After these, the main dialogue continues with the next line.")]
    [TextArea(1, 4)] public string[] linesIfA;
    [Tooltip("NPC lines spoken if the player picks Option B. After these, the main dialogue continues with the next line.")]
    [TextArea(1, 4)] public string[] linesIfB;
}

/// <summary>
/// A single beat in a dialogue tree — either an NPC speaker line or a player
/// choice point. Built at runtime from a TextAsset (via DialogueParser) or from
/// the legacy DialogueChoice[] / dialogueLines[] fields on Npc.
/// </summary>
public class DialogueElement
{
    public string speakerLine;    // set when this element is an NPC speaker line
    public ChoiceBranch choice;   // set when this element is a player choice
}

/// <summary>
/// One option in a player choice — the text the player picks, and the dialogue
/// sequence that plays after picking it.
/// </summary>
public class ChoiceOption
{
    public string optionText;
    public DialogueElement[] branch;
}

/// <summary>
/// An N-way branch attached to a DialogueElement.choice. Each option is a
/// distinct path; picking one runs that path's sequence. Branches can contain
/// further nested choices recursively.
/// </summary>
public class ChoiceBranch
{
    public ChoiceOption[] options;
}

/// <summary>
/// Parses an NPC's dialogue text file into a tree of DialogueElements.
///
/// Format:
///   • The file is a list of FULL, FLAT conversation transcripts. Each
///     conversation is what would happen if the player made specific choices.
///   • Conversations are separated by a line containing exactly "//".
///   • Inside a conversation, each non-empty line is one beat:
///       - "P: …" is a player utterance (will become a choice option at merge time).
///       - Anything else is an NPC speaker line.
///   • Blank lines inside a conversation are ignored — they're for readability.
///
/// Merge rule:
///   • The parser walks all conversations in lock-step.
///   • While they all agree on the next beat (same NPC line), that beat is shared
///     and appended to the merged sequence once.
///   • The moment they diverge, the divergent beat becomes a player choice node.
///     Conversations are grouped by their next beat's text — unique player
///     utterances become unique options. The merge then recurses inside each
///     group to keep merging that branch's shared NPC lines and to discover any
///     further nested choices.
///   • Branches don't re-merge: even if two branches happen to end with the same
///     line, they remain separate paths.
///
/// So if three conversations share their first two lines, then split into three
/// different "P:" lines, the player sees a 3-option choice at that point. If two
/// of those three choices later converge on shared NPC text and then diverge
/// again at another "P:" line, the parser nests a 2-option choice under the
/// first.
/// </summary>
public static class DialogueParser
{
    public static DialogueElement[] Parse(string text)
    {
        if (string.IsNullOrEmpty(text)) return new DialogueElement[0];

        var conversations = SplitConversations(text);
        if (conversations.Count == 0) return new DialogueElement[0];

        var eventLists = new List<DialogueEvent[]>(conversations.Count);
        for (int i = 0; i < conversations.Count; i++)
            eventLists.Add(ConversationToEvents(conversations[i]));

        var allIndices = new List<int>(eventLists.Count);
        for (int i = 0; i < eventLists.Count; i++) allIndices.Add(i);
        return Merge(eventLists, allIndices, 0);
    }

    class DialogueEvent
    {
        public bool isPlayer;
        public string text;
    }

    // Split the raw file into per-conversation lists of non-empty trimmed lines.
    // A line that's exactly "//" (after trimming) ends the current conversation
    // and starts a new one. Blank/whitespace lines are dropped entirely.
    static List<List<string>> SplitConversations(string text)
    {
        var result = new List<List<string>>();
        var current = new List<string>();
        var rawLines = text.Split('\n');
        for (int i = 0; i < rawLines.Length; i++)
        {
            string raw = rawLines[i];
            if (raw.Length > 0 && raw[raw.Length - 1] == '\r')
                raw = raw.Substring(0, raw.Length - 1);
            string trimmed = raw.Trim();
            if (trimmed == "//")
            {
                if (current.Count > 0)
                {
                    result.Add(current);
                    current = new List<string>();
                }
            }
            else if (!string.IsNullOrWhiteSpace(trimmed))
            {
                current.Add(trimmed);
            }
        }
        if (current.Count > 0) result.Add(current);
        return result;
    }

    static DialogueEvent[] ConversationToEvents(List<string> lines)
    {
        // Belt-and-suspenders: SplitConversations already drops blank lines, but
        // also drop anything that's effectively empty here — including lines that
        // are just "P:" with no text after the prefix, or NPC lines that ended up
        // as pure whitespace somehow. The merge step depends on every event
        // having real text, so an empty event leaking through would create an
        // invisible "choice option" with no label that nothing seems to advance.
        var events = new List<DialogueEvent>(lines.Count);
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            bool isPlayer = line.Length >= 2 && line[0] == 'P' && line[1] == ':';
            string text = isPlayer ? line.Substring(2).TrimStart() : line.Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;

            events.Add(new DialogueEvent { isPlayer = isPlayer, text = text });
        }
        return events.ToArray();
    }

    // Walk the conversations in `indices` (a subset of all conversations) in
    // lock-step from `startPos`. Append shared NPC lines to the result; the
    // moment any divergence appears, build a choice node grouping conversations
    // by their next beat's text, recurse into each group, then stop — the rest
    // of this sequence lives inside the chosen branch.
    static DialogueElement[] Merge(List<DialogueEvent[]> conversations, List<int> indices, int startPos)
    {
        var result = new List<DialogueElement>();
        int pos = startPos;

        while (true)
        {
            // Which conversations still have an event at this position?
            var active = new List<int>();
            for (int j = 0; j < indices.Count; j++)
            {
                int i = indices[j];
                if (pos < conversations[i].Length) active.Add(i);
            }
            if (active.Count == 0) break;

            // Do they all agree on the next beat?
            var first = conversations[active[0]][pos];
            bool allAgree = true;
            for (int j = 1; j < active.Count; j++)
            {
                var ev = conversations[active[j]][pos];
                if (ev.isPlayer != first.isPlayer || ev.text != first.text)
                {
                    allAgree = false;
                    break;
                }
            }

            if (allAgree && !first.isPlayer)
            {
                // Shared NPC speech — append once and advance everyone by one.
                result.Add(new DialogueElement { speakerLine = first.text });
                pos++;
            }
            else
            {
                // Divergence (or unanimous player line — still rendered as a
                // single-option choice for consistency). Group active
                // conversations by their next beat's text, preserving the order
                // of first appearance.
                var groupKeys = new List<string>();
                var groupMembers = new Dictionary<string, List<int>>();
                for (int j = 0; j < active.Count; j++)
                {
                    int i = active[j];
                    var ev = conversations[i][pos];
                    // Prefix with 'P'/'N' so a player line "Hi" and an NPC line
                    // "Hi" (very unlikely but possible) wouldn't collapse into
                    // a single group.
                    string key = (ev.isPlayer ? "P:" : "N:") + ev.text;
                    if (!groupMembers.ContainsKey(key))
                    {
                        groupMembers[key] = new List<int>();
                        groupKeys.Add(key);
                    }
                    groupMembers[key].Add(i);
                }

                var options = new ChoiceOption[groupKeys.Count];
                for (int k = 0; k < groupKeys.Count; k++)
                {
                    var subIndices = groupMembers[groupKeys[k]];
                    var firstInGroup = conversations[subIndices[0]][pos];
                    options[k] = new ChoiceOption
                    {
                        optionText = firstInGroup.text,
                        // Advance past the diverging event for this group.
                        branch = Merge(conversations, subIndices, pos + 1),
                    };
                }

                result.Add(new DialogueElement
                {
                    choice = new ChoiceBranch { options = options }
                });
                break; // everything after the choice lives in the branches
            }
        }

        return result.ToArray();
    }
}

/// <summary>
/// Attach to an NPC the player can talk to. SPACE while adjacent starts the
/// dialogue; each subsequent SPACE press advances to the next line; ESC, walking
/// out of range, or running out of lines all exit the conversation.
///
/// Two ways to author the dialogue:
///   • Drop a TextAsset into Dialogue File for the rich, multi-choice format.
///   • Leave Dialogue File empty and use the legacy Dialogue Lines + Choices
///     fields below for simple flat conversations with optional one-level
///     two-way choices.
/// </summary>
public class Npc : MonoBehaviour
{
    [Tooltip("Extra world-unit margin added to the player's bounds when checking adjacency.")]
    public float adjacencyMargin = 0.1f;

    [Header("Dialogue")]
    [Tooltip("Optional portrait image. Drag a Texture2D here (import the source PNG with " +
             "Texture Type = 'Default' instead of 'Sprite (2D and UI)' so it's available as a texture). " +
             "Texture2D is used so the field doesn't conflict with the animation window's sprite-drop prompt.")]
    public Texture2D portrait;

    [Tooltip("Optional text file (.txt). When set, overrides the Dialogue Lines and " +
             "Choices fields below. Format:\n" +
             "  • The file is a list of FULL conversations the player could possibly have.\n" +
             "  • Each conversation is separated by a line containing exactly '//'.\n" +
             "  • Inside a conversation, each non-empty line is one beat.\n" +
             "  • Lines starting with 'P:' are the player's utterance at that step.\n" +
             "  • All other lines are NPC speaker lines.\n" +
             "  • Blank lines are ignored (use them for readability).\n" +
             "The parser merges shared prefixes across conversations; at each spot " +
             "where the conversations diverge, the unique 'P:' utterances become " +
             "choice options. Choices may have any number of options (more than two), " +
             "and may be nested arbitrarily.")]
    public TextAsset dialogueFile;

    [Tooltip("One entry per line. The player advances through them by pressing SPACE; " +
             "the previous line disappears each time. Ignored when Dialogue File is set.")]
    [TextArea(2, 6)]
    public string[] dialogueLines = { "Hello there." };

    [Tooltip("Optional choice points. Each entry fires after a specific line index in " +
             "dialogueLines and presents two options. Ignored when Dialogue File is set.")]
    public DialogueChoice[] choices;

    [Tooltip("Font size for the dialogue text. Range is wide so big-text NPCs " +
             "(e.g. a giant shouting at the player) can crank this all the way up.")]
    [Range(12, 160)] public int fontSize = 40;

    [Tooltip("Optional custom font for this NPC's dialogue. Drag any imported Font " +
             "asset (Assets > Import New Asset > a .ttf or .otf file). When empty, " +
             "Unity's default GUI font is used. Tip: set the same font on every NPC " +
             "to get a global dialogue font without touching code.")]
    public Font dialogueFont;

    SpriteRenderer sr;
    Collider2D col;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<Collider2D>();
    }

    public bool IsAdjacentTo(Bounds playerBounds)
    {
        // Prefer the NPC's sprite bounds; fall back to its collider so an
        // invisible-trigger NPC (just a Collider2D) also works.
        Bounds npcBounds;
        if (sr != null) npcBounds = sr.bounds;
        else if (col != null) npcBounds = col.bounds;
        else return false;

        playerBounds.Expand(adjacencyMargin * 2f);
        return playerBounds.Intersects(npcBounds);
    }

    /// <summary>
    /// Build the tree GameDirector walks at runtime. Prefers the text file when
    /// present; otherwise converts the legacy flat dialogueLines + choices into
    /// an equivalent tree (a sequence of speaker-line elements with two-option
    /// choice elements inserted at their afterLineIndex positions).
    /// </summary>
    public DialogueElement[] BuildDialogueTree()
    {
        if (dialogueFile != null && !string.IsNullOrWhiteSpace(dialogueFile.text))
            return DialogueParser.Parse(dialogueFile.text);
        return FlatToTree(dialogueLines, choices);
    }

    static DialogueElement[] FlatToTree(string[] lines, DialogueChoice[] flatChoices)
    {
        if (lines == null || lines.Length == 0) return new DialogueElement[0];
        var result = new List<DialogueElement>(lines.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            result.Add(new DialogueElement { speakerLine = lines[i] });
            var fc = FindFlatChoiceAt(flatChoices, i);
            if (fc != null)
            {
                result.Add(new DialogueElement
                {
                    choice = new ChoiceBranch
                    {
                        options = new ChoiceOption[]
                        {
                            new ChoiceOption { optionText = fc.optionA, branch = LinesToSpeakerElements(fc.linesIfA) },
                            new ChoiceOption { optionText = fc.optionB, branch = LinesToSpeakerElements(fc.linesIfB) },
                        }
                    }
                });
            }
        }
        return result.ToArray();
    }

    static DialogueChoice FindFlatChoiceAt(DialogueChoice[] arr, int afterLineIndex)
    {
        if (arr == null) return null;
        for (int i = 0; i < arr.Length; i++)
            if (arr[i] != null && arr[i].afterLineIndex == afterLineIndex) return arr[i];
        return null;
    }

    static DialogueElement[] LinesToSpeakerElements(string[] lines)
    {
        if (lines == null) return new DialogueElement[0];
        var result = new DialogueElement[lines.Length];
        for (int i = 0; i < lines.Length; i++)
            result[i] = new DialogueElement { speakerLine = lines[i] };
        return result;
    }
}
