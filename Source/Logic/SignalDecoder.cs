namespace UBOAT.Mods.RadioNavigation
{
    using System;

    /// <summary>
    /// Dominant-station signal decoding behind SonneController (P5.6 slice
    /// SC-1, F02). The controller keeps fleet wiring, tuning and the gate
    /// inputs (zone/gain/dominant index); the dominant gate decision and
    /// the transcript ownership live here so feed, clear and counters run
    /// offline in SignalDecoderTests. The AddElement/FinishSequence names
    /// are a SourceCheck contract (SignalTranscript_OwnsBuffers): the
    /// controller handlers must invoke them. Main-thread only. Pure C#.
    /// </summary>
    public sealed class SignalDecoder
    {
        private readonly SignalTranscript transcript = new SignalTranscript();

        // Probe diagnostics: callsign-flagged elements seen by THIS decoder
        // vs how many passed the dominant gate. Total==0 -> keyer events not
        // routed here; Processed==0 with Total>0 -> gate (zone/gain/dominant)
        // was false during the Callsign phase.
        public static int CallsignSeenTotal;
        public static int CallsignSeenProcessed;

        // Probe diagnostics: how many times the transcript was wiped and by
        // whom (first stack captured). A mid-Callsign clear explains a
        // silently lost decode (buffer reset -> length mismatch -> no text).
        public static int ClearCount;
        public static string FirstClearStack = "";

        public string RichText => transcript.RichText;

        public string DecodedText => transcript.DecodedText;

        public void AddElement(NavigationElementType elemType, bool isCallsign,
            BeaconStation station, bool processingDominant)
        {
            if (isCallsign)
            {
                CallsignSeenTotal++;
                if (processingDominant)
                    CallsignSeenProcessed++;
            }
            if (!processingDominant)
                return;

            transcript.AddElement(elemType, isCallsign, station);
        }

        public void FinishSequence(BeaconStation station, bool processingDominant)
        {
            if (!processingDominant)
                return;

            transcript.FinishSequence(station);
        }

        public void Clear()
        {
            ClearCount++;
            if (FirstClearStack.Length == 0)
                FirstClearStack = Environment.StackTrace;
            transcript.Clear();
        }

        public int CountDots()
        {
            return transcript.CountDots();
        }

        public int CountDashes()
        {
            return transcript.CountDashes();
        }

        // Live diagnostic: how many callsign-typed chars reached the
        // transcript. Zero while dots/dashes grow means the keyer never
        // marked an element as callsign (phase or sequence bug).
        public int CountCallsignChars()
        {
            return transcript.CountCallsignChars();
        }
    }
}
