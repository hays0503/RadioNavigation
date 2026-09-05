namespace UBOAT.Mods.RadioNavigation
{
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

        public string RichText => transcript.RichText;

        public string DecodedText => transcript.DecodedText;

        public void AddElement(NavigationElementType elemType, bool isCallsign,
            BeaconStation station, bool processingDominant)
        {
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
    }
}
