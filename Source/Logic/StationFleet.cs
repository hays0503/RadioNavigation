using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Station object fleet: states, schedulers, keyers, carriers plus
    /// keyer event wiring, behind SonneController.Awake/OnDestroy.
    /// Extracted (SRP — station lifecycle is not controller logic) so
    /// fleet build, callback routing and symmetric Unwire run offline in
    /// StationFleetTests. Callbacks route by station index.
    /// </summary>
    public sealed class StationFleet
    {
        private readonly Action<NavigationElementType, bool>[] elementHandlers;
        private readonly Action[] sequenceHandlers;

        public StationState[] States { get; }
        public StationScheduler[] Schedulers { get; }
        public MorseKeyer[] Keyers { get; }
        public CarrierGenerator[] Carriers { get; }

        public StationFleet(Action<int, NavigationElementType, bool> onElement, Action<int> onSequence)
        {
            var beaconStations = BeaconData.Stations;
            States = new StationState[beaconStations.Length];
            Schedulers = new StationScheduler[beaconStations.Length];
            Keyers = new MorseKeyer[beaconStations.Length];
            Carriers = new CarrierGenerator[beaconStations.Length];
            elementHandlers = new Action<NavigationElementType, bool>[beaconStations.Length];
            sequenceHandlers = new Action[beaconStations.Length];

            for (int i = 0; i < beaconStations.Length; i++)
            {
                StationCallsignResolver.TryGet(beaconStations[i], out string callsign);

                var config = ConsolCycleConfig.GetForCycleDuration(beaconStations[i].CycleDurationSeconds);
                var initialPattern = SonneBeam.BuildPattern(0, false);

                var scheduler = new StationScheduler(callsign)
                {
                    Config = config,
                    CurrentConsolPattern = initialPattern
                };
                var keyer = new MorseKeyer();
                var carrier = new CarrierGenerator(AudioSynthesisEngine.DeviceSampleRate);

                Schedulers[i] = scheduler;
                Keyers[i] = keyer;
                Carriers[i] = carrier;

                States[i] = new StationState
                {
                    InBeam = false,
                    TransitionIndex = config.MaxNavigationCount,
                    Gain = 0f,
                    BeatFrequency = Constants.BfoBaseHz,
                    Config = config,
                    DotCount = 0,
                    CurrentBearing = 0,
                    BearingToBoat = 0,
                    Pattern = SonneSignalGenerator.ToNavigationPattern(initialPattern)
                };

                int stationIndex = i;
                var elementHandler = new Action<NavigationElementType, bool>((elemType, isCallsign) => onElement(stationIndex, elemType, isCallsign));
                var sequenceHandler = new Action(() => onSequence(stationIndex));
                elementHandlers[i] = elementHandler;
                sequenceHandlers[i] = sequenceHandler;
                keyer.OnElementCompleted += elementHandler;
                keyer.OnSequenceFinished += sequenceHandler;
            }
        }

        /// <summary>Symmetric unsubscribe for teardown (no handler leaks).</summary>
        public void Unwire()
        {
            if (Keyers.IsNotNull() && elementHandlers.IsNotNull() && sequenceHandlers.IsNotNull())
            {
                for (int i = 0; i < Keyers.Length; i++)
                {
                    if (Keyers[i].IsNotNull())
                    {
                        Keyers[i].OnElementCompleted -= elementHandlers[i];
                        Keyers[i].OnSequenceFinished -= sequenceHandlers[i];
                    }
                }
            }
        }
    }
}
