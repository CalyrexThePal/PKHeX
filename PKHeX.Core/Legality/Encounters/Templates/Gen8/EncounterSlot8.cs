using static PKHeX.Core.OverworldCorrelation8Requirement;

namespace PKHeX.Core;

/// <summary>
/// Encounter Slot found in <see cref="GameVersion.SWSH"/>.
/// </summary>
public sealed record EncounterSlot8(EncounterArea8 Parent, ushort Species, byte Form, byte LevelMin, byte LevelMax, AreaWeather8 Weather, AreaSlotType8 Type)
    : IEncounterable, IEncounterMatch, IEncounterConvertible<PK8>, IOverworldCorrelation8
{
    public int Generation => 8;
    public EntityContext Context => EntityContext.Gen8;
    public bool EggEncounter => false;
    public Ball FixedBall => Ball.None;
    public AbilityPermission Ability => AbilityPermission.Any12;
    public Shiny Shiny => Shiny.Random;
    public bool IsShiny => false;
    public int EggLocation => 0;

    public string Name => $"Wild Encounter ({Version})";
    public string LongName => $"{Name} [{Type}] - {Weather.ToString().Replace("_", string.Empty)}";
    public GameVersion Version => Parent.Version;
    public int Location => Parent.Location;

    // Fishing are only from the hidden table (not symbol).
    public bool CanEncounterViaFishing => Type.CanEncounterViaFishing(Weather);
    public bool CanEncounterViaCurry
    {
        get
        {
            if (!Type.CanEncounterViaCurry())
                return false;

            if ((Weather & AreaWeather8.All) == 0)
                return false;

            if (EncounterArea8.IsWildArea(Location))
                return false;

            return true;
        }
    }

    #region Generating
    PKM IEncounterConvertible.ConvertToPKM(ITrainerInfo tr, EncounterCriteria criteria) => ConvertToPKM(tr, criteria);
    PKM IEncounterConvertible.ConvertToPKM(ITrainerInfo tr) => ConvertToPKM(tr);
    public PK8 ConvertToPKM(ITrainerInfo tr) => ConvertToPKM(tr, EncounterCriteria.Unrestricted);
    public PK8 ConvertToPKM(ITrainerInfo tr, EncounterCriteria criteria)
    {
        int lang = (int)Language.GetSafeLanguage(Generation, (LanguageID)tr.Language);
        var form = GetWildForm(Form);
        var pk = new PK8
        {
            Species = Species,
            Form = form,
            CurrentLevel = LevelMin,
            Met_Location = Location,
            Met_Level = LevelMin,
            Version = (byte)Version,
            MetDate = EncounterDate.GetDateSwitch(),
            Ball = (byte)Ball.Poke,

            Language = lang,
            OT_Name = tr.OT,
            OT_Gender = tr.Gender,
            ID32 = tr.ID32,
            Nickname = SpeciesName.GetSpeciesNameGeneration(Species, lang, Generation),
            OT_Friendship = PersonalTable.SWSH[Species, form].BaseFriendship,
        };
        SetPINGA(pk, criteria);
        EncounterUtil1.SetEncounterMoves(pk, Version, LevelMin);

        bool symbol = Parent.PermitCrossover;
        if (!symbol && Location is 30 or 54 && (Weather & AreaWeather8.Fishing) == 0)
            pk.RibbonMarkCurry = true;

        if (Weather is AreaWeather8.Heavy_Fog && EncounterArea8.IsBoostedArea60Fog(Location))
            pk.CurrentLevel = pk.Met_Level = EncounterArea8.BoostLevel;
        pk.ResetPartyStats();
        return pk;
    }

    private byte GetWildForm(byte form)
    {
        if (form == EncounterUtil1.FormRandom) // flagged as totally random
            return (byte)Util.Rand.Next(PersonalTable.SWSH[Species].FormCount);
        return form;
    }

    #endregion

    private void SetPINGA(PK8 pk, EncounterCriteria criteria)
    {
        bool symbol = Parent.PermitCrossover;
        var c = symbol ? EncounterCriteria.Unrestricted : criteria;

        var req = GetRequirement(pk);
        if (req != MustHave)
        {
            var pi = PersonalTable.SWSH[Species, Form];
            var rand = Util.Rand;
            pk.EncryptionConstant = rand.Rand32();
            pk.PID = rand.Rand32();
            criteria.SetRandomIVs(pk);
            pk.Gender = criteria.GetGender(-1, pi);
            pk.Nature = pk.StatNature = (int)criteria.GetNature(Nature.Random);
            pk.RefreshAbility(criteria.GetAbilityFromNumber(Ability));
            return;
        }
        // Don't bother honoring shiny state.
        Overworld8RNG.ApplyDetails(pk, c, Shiny.Random);
        pk.RefreshAbility(criteria.GetAbilityFromNumber(Ability));
    }

    public OverworldCorrelation8Requirement GetRequirement(PKM pk)
    {
        if (Parent.PermitCrossover)
            return MustHave; // symbol walking overworld

        bool curry = pk is IRibbonSetMark8 {RibbonMarkCurry: true} || (pk.Species == (int)Core.Species.Shedinja && pk is IRibbonSetAffixed { AffixedRibbon:(int)RibbonIndex.MarkCurry});
        if (curry)
            return MustNotHave;

        // Tree encounters are generated via the global seed, not the u32
        if ((Weather & AreaWeather8.Shaking_Trees) != 0)
        {
            // Some tree encounters are present in the regular encounters.
            return Weather == AreaWeather8.Shaking_Trees
                ? MustNotHave
                : CanBeEither;
        }

        return MustHave;
    }

    public bool IsOverworldCorrelationCorrect(PKM pk)
    {
        var flawless = GetFlawlessIVCount(pk.Met_Level);
        return Overworld8RNG.ValidateOverworldEncounter(pk, flawless: flawless);
    }

    private int GetFlawlessIVCount(int met)
    {
        const int none = 0;
        const int any023 = -1;

        // Brilliant encounters are boosted to max level for the slot.
        if (met < LevelMax)
            return none;

        if (Parent.PermitCrossover)
            return any023; // Symbol
        if ((Weather & AreaWeather8.Fishing) != 0)
            return any023; // Fishing
        return none; // Hidden
    }

    #region Matching

    public bool IsMatchExact(PKM pk, EvoCriteria evo)
    {
        if (Form != evo.Form && Species is not (int)Core.Species.Rotom)
            return false;

        var metLocation = pk.Met_Location;
        if (Location != metLocation && !EncounterArea8.CanCrossoverTo(Location, metLocation, Type))
            return false;

        var met = pk.Met_Level;
        if (met == EncounterArea8.BoostLevel && EncounterArea8.IsBoostedArea60(Location))
            return true;

        if (!this.IsLevelWithinRange(met))
            return false;

        if (Weather is AreaWeather8.Heavy_Fog && EncounterArea8.IsWildArea8(Location))
            return false; // Heavy Fog not available until post-game, which would return true above.

        return true;
    }

    public EncounterMatchRating GetMatchRating(PKM pk)
    {
        bool isHidden = pk.AbilityNumber == 4;
        if (isHidden && this.IsPartialMatchHidden(pk.Species, Species))
            return EncounterMatchRating.PartialMatch;

        if (pk is IRibbonSetMark8 m)
        {
            if (m.RibbonMarkCurry && (Weather & AreaWeather8.All) == 0)
                return EncounterMatchRating.DeferredErrors;
            if (m.RibbonMarkFishing && (Weather & AreaWeather8.Fishing) == 0)
                return EncounterMatchRating.DeferredErrors;

            // Check if it has a mark and the weather does not permit the mark.
            // Tree/Fishing slots should be deferred here and are checked later.
            if (!Weather.IsMarkCompatible(m))
                return EncounterMatchRating.DeferredErrors;

            // Galar Mine hidden encounters can only be found via Curry or Fishing.
            if (Location is (30 or 54) && Type is AreaSlotType8.HiddenMain && !m.RibbonMarkCurry && !Type.CanEncounterViaFishing(Weather))
                return EncounterMatchRating.DeferredErrors;
        }

        var req = GetRequirement(pk);
        return req switch
        {
            MustHave when !IsOverworldCorrelationCorrect(pk) => EncounterMatchRating.DeferredErrors,
            MustNotHave when IsOverworldCorrelationCorrect(pk) => EncounterMatchRating.DeferredErrors,
            _ => EncounterMatchRating.Match,
        };
        #endregion
    }
}
