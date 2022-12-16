using BepInEx;
using BepInEx.Configuration;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.AddressableAssets;
using System.Collections.Generic;

namespace AugmentedHoldout
{
  [BepInPlugin("com.Nuxlar.AugmentedHoldout", "AugmentedHoldout", "1.0.5")]

  public class AugmentedHoldout : BaseUnityPlugin
  {
    public ConfigEntry<float> creditMultiplier;
    public static ConfigFile RoRConfig { get; set; }
    private bool tpStarted = false;
    private bool shipStarted = false;
    // Monster credit manipulators { Min, Max }
    private readonly float[] monsterCreditBase = { 15, 40 };
    private readonly float[] monsterCreditInterval = { 10, 20 };
    private readonly float[] rerollSpawnIntervalEclipse = { 2.333f, 4.333f };
    private readonly float[] rerollSpawnIntervalMonsoon = { 0.333f, 2.333f };
    // Timers
    private float monsterCreditTimer = 0;
    // SpawnCards
    private static SpawnCard jailer = Addressables.LoadAssetAsync<SpawnCard>("RoR2/DLC1/VoidJailer/cscVoidJailer.asset").WaitForCompletion();
    private static SpawnCard devastator = Addressables.LoadAssetAsync<SpawnCard>("RoR2/DLC1/VoidMegaCrab/cscVoidMegaCrab.asset").WaitForCompletion();
    private static DirectorCardCategorySelection dccsVoidStageMonsters = Addressables.LoadAssetAsync<DirectorCardCategorySelection>("RoR2/DLC1/voidstage/dccsVoidStageMonsters.asset").WaitForCompletion();
    private bool eclipseEnabled;

    public void Awake()
    {
      RoRConfig = new ConfigFile(Paths.ConfigPath + "\\com.Nuxlar.AugmentedHoldout.cfg", true);
      creditMultiplier = RoRConfig.Bind("General", "Credit Multiplier", 1.25f, "A multiplier for the rate at which more difficult monsters spawn; 1 is the default rate.");
      On.RoR2.HoldoutZoneController.Start += HoldoutZoneControllerStart;
      On.RoR2.HoldoutZoneController.OnDisable += HoldoutZoneControllerOnDisable;
      On.RoR2.CombatDirector.FixedUpdate += CombatDirectorFixedUpdate;
      On.RoR2.CombatDirector.Simulate += CombatDirectorSimulate;
      On.RoR2.Run.Start += RunStart;
      // Moon
      On.EntityStates.MoonElevator.MoonElevatorBaseState.OnEnter += MoonElevatorBaseStateOnEnter;
      On.RoR2.MoonBatteryMissionController.OnBatteryCharged += OnPillarCharged;
      // Void Locus
      On.RoR2.VoidStageMissionController.RequestFog += RequestFog;
    }

    private void RunStart(On.RoR2.Run.orig_Start orig, RoR2.Run self)
    {
      orig(self);
      eclipseEnabled = Run.instance.selectedDifficulty >= DifficultyIndex.Eclipse2;
    }

    private VoidStageMissionController.FogRequest RequestFog(On.RoR2.VoidStageMissionController.orig_RequestFog orig, RoR2.VoidStageMissionController self, IZone zone)
    {
      return null;
    }

    private void MoonElevatorBaseStateOnEnter(On.EntityStates.MoonElevator.MoonElevatorBaseState.orig_OnEnter orig, EntityStates.MoonElevator.MoonElevatorBaseState self)
    {
      orig(self);
      self.outer.SetNextState(new EntityStates.MoonElevator.Ready());
    }

    private void OnPillarCharged(On.RoR2.MoonBatteryMissionController.orig_OnBatteryCharged orig, RoR2.MoonBatteryMissionController self, HoldoutZoneController holdoutZone)
    {
      orig(self, holdoutZone);
      if (NetworkServer.active)
      {
        Vector3 rewardPositionOffset = new Vector3(0f, 3f, 0f);
        float pearlOverwriteChance = 15f;

        PickupIndex pickupIndex = SelectItem();
        ItemTier tier = PickupCatalog.GetPickupDef(pickupIndex).itemTier;
        if (pickupIndex != PickupIndex.none)
        {

          PickupDef pickupDef = PickupCatalog.GetPickupDef(pickupIndex);

          int participatingPlayerCount = Run.instance.participatingPlayerCount;
          if (participatingPlayerCount != 0 && holdoutZone.transform)
          {
            int num = participatingPlayerCount;

            float angle = 360f / (float)num;
            Vector3 vector = Quaternion.AngleAxis((float)UnityEngine.Random.Range(0, 360), Vector3.up) * (Vector3.up * 40f + Vector3.forward * 5f);
            Quaternion rotation = Quaternion.AngleAxis(angle, Vector3.up);

            int k = 0;
            while (k < num)
            {
              PickupIndex pickupOverwrite = PickupIndex.none;
              bool overwritePickup = false;
              if (tier != ItemTier.Tier3)
              {
                float pearlChance = pearlOverwriteChance;
                float total = pearlChance;
                if (Run.instance.bossRewardRng.RangeFloat(0f, 100f) < pearlChance)
                {
                  pickupOverwrite = SelectPearl();
                }

                overwritePickup = !(pickupOverwrite == PickupIndex.none);
              }
              PickupDropletController.CreatePickupDroplet(overwritePickup ? pickupOverwrite : pickupIndex, holdoutZone.transform.position + rewardPositionOffset, vector);
              k++;
              vector = rotation * vector;
            }
          }
        }
      }
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
        DirectorSpawnRequest directorSpawnRequest2 = new DirectorSpawnRequest(devastator, placementRule, Run.instance.runRNG);
        directorSpawnRequest2.teamIndexOverride = new TeamIndex?(TeamIndex.Void);

        GameObject spawnedDevastator = devastator.DoSpawn(new Vector3(369, -174, 446), Quaternion.identity, directorSpawnRequest2).spawnedInstance;
        GameObject spawnedJailer = jailer.DoSpawn(new Vector3(254, -171.5f, 433), Quaternion.identity, directorSpawnRequest).spawnedInstance;
        // GameObject spawnedJailer2 = jailer.DoSpawn(new Vector3(296, -172, 321), Quaternion.identity, directorSpawnRequest).spawnedInstance;

        NetworkServer.Spawn(spawnedDevastator);
        NetworkServer.Spawn(spawnedJailer);
        //NetworkServer.Spawn(spawnedJailer2);
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
      if (tpStarted && eclipseEnabled)
      {
        self.minRerollSpawnInterval = rerollSpawnIntervalEclipse[0];
        self.maxRerollSpawnInterval = rerollSpawnIntervalEclipse[1];
      }
      else if (tpStarted)
      {
        self.minRerollSpawnInterval = rerollSpawnIntervalMonsoon[0];
        self.maxRerollSpawnInterval = rerollSpawnIntervalMonsoon[1];
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

    private static PickupIndex SelectPearl()
    {
      PickupIndex pearlIndex = PickupCatalog.FindPickupIndex(RoR2Content.Items.Pearl.itemIndex);
      PickupIndex shinyPearlIndex = PickupCatalog.FindPickupIndex(RoR2Content.Items.ShinyPearl.itemIndex);
      bool pearlAvailable = pearlIndex != PickupIndex.none && Run.instance.IsItemAvailable(RoR2Content.Items.Pearl.itemIndex);
      bool shinyPearlAvailable = shinyPearlIndex != PickupIndex.none && Run.instance.IsItemAvailable(RoR2Content.Items.ShinyPearl.itemIndex);

      PickupIndex toReturn = PickupIndex.none;
      if (pearlAvailable && shinyPearlAvailable)
      {
        toReturn = pearlIndex;
        if (Run.instance.bossRewardRng.RangeFloat(0f, 100f) <= 20f)
        {
          toReturn = shinyPearlIndex;
        }
      }
      else
      {
        if (pearlAvailable)
        {
          toReturn = pearlIndex;
        }
        else if (shinyPearlAvailable)
        {
          toReturn = shinyPearlIndex;
        }
      }
      return toReturn;
    }

    //Yellow Chance is handled after selecting item
    private static PickupIndex SelectItem()
    {
      float whiteChance = 50f;
      float greenChance = 40f;
      float redChance = 10f;
      float lunarChance = 0f;

      List<PickupIndex> list;
      Xoroshiro128Plus bossRewardRng = Run.instance.bossRewardRng;
      PickupIndex selectedPickup = PickupIndex.none;

      float total = whiteChance + greenChance + redChance + lunarChance;

      if (bossRewardRng.RangeFloat(0f, total) <= whiteChance)//drop white
      {
        list = Run.instance.availableTier1DropList;
      }
      else
      {
        total -= whiteChance;
        if (bossRewardRng.RangeFloat(0f, total) <= greenChance)//drop green
        {
          list = Run.instance.availableTier2DropList;
        }
        else
        {
          total -= greenChance;
          if ((bossRewardRng.RangeFloat(0f, total) <= redChance))
          {
            list = Run.instance.availableTier3DropList;
          }
          else
          {
            list = Run.instance.availableLunarCombinedDropList;
          }

        }
      }
      if (list.Count > 0)
      {
        selectedPickup = bossRewardRng.NextElementUniform<PickupIndex>(list);
      }
      return selectedPickup;
    }
  }
}