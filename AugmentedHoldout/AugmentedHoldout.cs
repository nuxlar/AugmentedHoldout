using BepInEx;
using BepInEx.Configuration;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.AddressableAssets;

namespace AugmentedHoldout
{
  [BepInPlugin("com.Nuxlar.AugmentedHoldout", "AugmentedHoldout", "1.0.2")]

  public class AugmentedHoldout : BaseUnityPlugin
  {
    public ConfigEntry<float> creditMultiplier;
    public ConfigEntry<bool> enableDuringEclipse;
    public static ConfigFile RoRConfig { get; set; }
    private bool tpStarted = false;
    private bool shipStarted = false;
    // Monster credit manipulators { Min, Max }
    private readonly float[] monsterCreditBase = { 15, 40 };
    private readonly float[] monsterCreditInterval = { 10, 20 };
    private readonly float[] rerollSpawnInterval = { 2.333f, 4.333f };
    // Timers
    private float monsterCreditTimer = 0;
    // SpawnCards
    SpawnCard jailer = Addressables.LoadAssetAsync<SpawnCard>("RoR2/DLC1/VoidJailer/cscVoidJailer.asset").WaitForCompletion();
    SpawnCard devastator = Addressables.LoadAssetAsync<SpawnCard>("RoR2/DLC1/VoidMegaCrab/cscVoidMegaCrab.asset").WaitForCompletion();
    DirectorCardCategorySelection dccsVoidStageMonsters = Addressables.LoadAssetAsync<DirectorCardCategorySelection>("RoR2/DLC1/voidstage/dccsVoidStageMonsters.asset").WaitForCompletion();

    public void Awake()
    {
      RoRConfig = new ConfigFile(Paths.ConfigPath + "\\com.Nuxlar.AugmentedHoldout.cfg", true);
      creditMultiplier = RoRConfig.Bind("General", "Credit Multiplier", 1.25f, "A multiplier for the rate at which more difficult monsters spawn; 1 is the default rate.");
      enableDuringEclipse = RoRConfig.Bind("General", "TeleExpand Eclipse", false, "Should the teleporter expand after defeating the boss in Eclipse?");
      On.RoR2.HoldoutZoneController.Start += HoldoutZoneControllerStart;
      On.RoR2.HoldoutZoneController.OnDisable += HoldoutZoneControllerOnDisable;
      On.RoR2.CombatDirector.FixedUpdate += CombatDirectorFixedUpdate;
      On.RoR2.CombatDirector.Simulate += CombatDirectorSimulate;
      On.RoR2.TeleporterInteraction.UpdateMonstersClear += TeleporterInteractionUpdateMonstersClear;
    }

    private void HoldoutZoneControllerStart(On.RoR2.HoldoutZoneController.orig_Start orig, RoR2.HoldoutZoneController self)
    {
      orig(self);
      tpStarted = true;
      if (self.inBoundsObjectiveToken == "OBJECTIVE_MOON_CHARGE_DROPSHIP")
      {
        shipStarted = true;
        // Spawn a Devastator and 2 Jailers because spawning seems to be slow
        DirectorPlacementRule placementRule = new DirectorPlacementRule();
        placementRule.placementMode = DirectorPlacementRule.PlacementMode.Approximate;
        DirectorSpawnRequest directorSpawnRequest = new DirectorSpawnRequest(jailer, placementRule, Run.instance.runRNG);
        directorSpawnRequest.teamIndexOverride = new TeamIndex?(TeamIndex.Void);
        DirectorPlacementRule placementRule2 = new DirectorPlacementRule();
        placementRule2.placementMode = DirectorPlacementRule.PlacementMode.Approximate;
        DirectorSpawnRequest directorSpawnRequest2 = new DirectorSpawnRequest(jailer, placementRule, Run.instance.runRNG);
        directorSpawnRequest2.teamIndexOverride = new TeamIndex?(TeamIndex.Void);
        GameObject spawnedDevastator = devastator.DoSpawn(new Vector3(369, -174, 446), Quaternion.identity, directorSpawnRequest).spawnedInstance;
        GameObject spawnedJailer1 = jailer.DoSpawn(new Vector3(254, -171.5f, 433), Quaternion.identity, directorSpawnRequest).spawnedInstance;
        GameObject spawnedJailer2 = jailer.DoSpawn(new Vector3(296, -172, 321), Quaternion.identity, directorSpawnRequest).spawnedInstance;
        NetworkServer.Spawn(spawnedDevastator);
        NetworkServer.Spawn(spawnedJailer1);
        NetworkServer.Spawn(spawnedJailer2);
      }
      //"Teleporter1(Clone)" "LunarTeleporter Variant(Clone)"
      // moon pillar MoonBatteryDesign MoonBatteryBlood MoonBatterySoul MoonBatteryMass (some number)
    }

    private void HoldoutZoneControllerOnDisable(On.RoR2.HoldoutZoneController.orig_OnDisable orig, RoR2.HoldoutZoneController self)
    {
      tpStarted = false;
      if (self.inBoundsObjectiveToken == "OBJECTIVE_MOON_CHARGE_DROPSHIP")
        shipStarted = false;
      orig(self);
    }

    private void CombatDirectorFixedUpdate(On.RoR2.CombatDirector.orig_FixedUpdate orig, RoR2.CombatDirector self)
    {
      if (tpStarted)
      {
        self.minRerollSpawnInterval = rerollSpawnInterval[0];
        self.maxRerollSpawnInterval = rerollSpawnInterval[1];
      }
      else
      {
        // set to Vanilla
        self.minRerollSpawnInterval = 4.5f;
        self.maxRerollSpawnInterval = 9;
      }
      orig(self);
    }

    private void CombatDirectorSimulate(On.RoR2.CombatDirector.orig_Simulate orig, RoR2.CombatDirector self, float deltaTime)
    {
      if (tpStarted)
      {

        if (shipStarted)
        {
          self.monsterCards = dccsVoidStageMonsters;
          self.teamIndex = TeamIndex.Void;
        }

        // Update interval
        monsterCreditTimer -= deltaTime;

        // Update credit for faster spawns
        if (monsterCreditTimer < 0)
        {

          // Spawn boost min max
          float minBaseCredit = monsterCreditBase[0];
          float maxBaseCredit = monsterCreditBase[1] + ((Run.instance.stageClearCount + 1) * creditMultiplier.Value);

          // Scale difficulty
          self.monsterCredit *= creditMultiplier.Value > 0 ? creditMultiplier.Value : 1;

          if (self.monsterCredit < minBaseCredit)
            self.monsterCredit = Random.Range(minBaseCredit, maxBaseCredit);

          // Reset timer
          monsterCreditTimer = Random.Range(monsterCreditInterval[0], monsterCreditInterval[1]);
        }
      }
      orig(self, deltaTime);
    }
    private void TeleporterInteractionUpdateMonstersClear(On.RoR2.TeleporterInteraction.orig_UpdateMonstersClear orig, RoR2.TeleporterInteraction self)
    {
      orig(self);
      //Minimum charge of 5% to prevent it from instantly expanding when the tele starts before boss is spawned
      if (self.monstersCleared && self.holdoutZoneController && self.activationState == TeleporterInteraction.ActivationState.Charging && self.chargeFraction > 0.05f)
      {
        bool eclipseEnabled = Run.instance.selectedDifficulty >= DifficultyIndex.Eclipse2;
        if (enableDuringEclipse.Value || !eclipseEnabled)
        {
          if (Util.GetItemCountForTeam(self.holdoutZoneController.chargingTeam, RoR2Content.Items.FocusConvergence.itemIndex, true, true) <= 0)
          {
            self.holdoutZoneController.currentRadius = 1000000f;
          }
        }
      }
    }
  }
}