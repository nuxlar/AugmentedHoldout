using BepInEx;
using BepInEx.Configuration;
using RoR2;
using UnityEngine;

namespace AugmentedHoldout
{
  [BepInPlugin("com.Nuxlar.AugmentedHoldout", "AugmentedHoldout", "1.0.0")]

  public class AugmentedHoldout : BaseUnityPlugin
  {
    public ConfigEntry<float> creditMultiplier;
    public static ConfigFile RoRConfig { get; set; }
    private bool tpStarted = false;
    // Monster credit manipulators { Min, Max }
    private readonly float[] monsterCreditBase = { 15, 40 };
    private readonly float[] monsterCreditInterval = { 10, 20 };
    private readonly float[] rerollSpawnInterval = { 2.25f, 4.5f };
    // Timers
    private float monsterCreditTimer = 0;

    public void Awake()
    {
      RoRConfig = new ConfigFile(Paths.ConfigPath + "\\com.Nuxlar.AugmentedHoldout.cfg", true);
      creditMultiplier = RoRConfig.Bind("General", "Credit Multiplier", 1.25f, "A multiplier for the rate at which more difficult monsters spawn; 1 is the default rate.");
      On.RoR2.HoldoutZoneController.Start += HoldoutZoneControllerStart;
      On.RoR2.HoldoutZoneController.OnDisable += HoldoutZoneControllerOnDisable;
      On.RoR2.CombatDirector.FixedUpdate += CombatDirectorFixedUpdate;
      On.RoR2.CombatDirector.Simulate += CombatDirectorSimulate;
    }

    private void HoldoutZoneControllerStart(On.RoR2.HoldoutZoneController.orig_Start orig, RoR2.HoldoutZoneController self)
    {
      orig(self);
      tpStarted = true;
    }

    private void HoldoutZoneControllerOnDisable(On.RoR2.HoldoutZoneController.orig_OnDisable orig, RoR2.HoldoutZoneController self)
    {
      tpStarted = false;
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
  }
}