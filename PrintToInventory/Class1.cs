using BepInEx;
using EntityStates;
using EntityStates.Scrapper;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

namespace R2API.Utils
{
	[AttributeUsage(AttributeTargets.Assembly)]
	public class ManualNetworkRegistrationAttribute : Attribute
	{
	}
}

namespace PrintToInventory
{
	[BepInDependency("com.funkfrog_sipondo.sharesuite", BepInDependency.DependencyFlags.SoftDependency)]
	[BepInPlugin("com.Moffein.PrintToInventory", "PrintToInventory", "1.0.0")]

    public class PrintToInventory : BaseUnityPlugin
    {
		//private static SceneDef bazaarDef = Addressables.LoadAssetAsync<SceneDef>("RoR2/Base/bazaar/bazaar.asset").WaitForCompletion();
		public static bool affectScrappers = true;
		public static bool affectPrinters = true;
		public static bool affectCauldrons = true;
		public static bool affectCleanse = true;

		public void Awake()
        {
			if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.funkfrog_sipondo.sharesuite"))
            {
				Debug.LogError("PrintToInventory: ShareSuite detected! PrintToInventory will be disabled due to incompatibilities.");
				return;
			}

			affectCauldrons = Config.Bind("General", "Cauldron", true, "Affect this interactable.").Value;
			affectPrinters = Config.Bind("General", "Printer", true, "Affect this interactable.").Value;
			affectScrappers = Config.Bind("General", "Scrapper", true, "Affect this interactable.").Value;
			affectCleanse = Config.Bind("General", "Cleansing Pool", true, "Affect this interactable.").Value;

			if (affectCauldrons || affectPrinters || affectCleanse) On.RoR2.ShopTerminalBehavior.DropPickup += ShopTerminalBehavior_DropPickup;
			if (affectScrappers)
            {
				IL.EntityStates.Scrapper.ScrappingToIdle.OnEnter += (il) =>
				{
					ILCursor c = new ILCursor(il);
					c.GotoNext(x => x.MatchLdloc(0), x => x.MatchLdsfld(typeof(PickupIndex), "none"));
					c.Index++;
					c.Emit(OpCodes.Ldarg_0);
					c.EmitDelegate<Func<PickupIndex, ScrappingToIdle, PickupIndex>>((pickupIndex, self) =>
					{
						if (pickupIndex != PickupIndex.none && self.scrapperController && self.scrapperController.interactor)
                        {
							CharacterBody cb = self.scrapperController.interactor.GetComponent<CharacterBody>();
							if (cb && cb.inventory)
							{
								PickupDef pd = PickupCatalog.GetPickupDef(pickupIndex);
								if (pd != null && pd.itemIndex != ItemIndex.None)
								{
									cb.inventory.GiveItem(pd.itemIndex, self.scrapperController.itemsEaten);
									self.scrapperController.itemsEaten = 0;
									return PickupIndex.none;
								}
							}
						}
						return pickupIndex;
					});
					//c.Emit(OpCodes.Stloc_0);
					//c.Emit(OpCodes.Ldloc_0);
				};
            }
		}

        private void ShopTerminalBehavior_DropPickup(On.RoR2.ShopTerminalBehavior.orig_DropPickup orig, ShopTerminalBehavior self)
        {
			bool addedToInventory = false;
			if (NetworkServer.active)
			{
				PurchaseInteraction pi = self.GetComponent<PurchaseInteraction>();
				if (pi && pi.lastActivator)
				{
					bool isCauldron = affectCauldrons && pi.displayNameToken == "BAZAAR_CAULDRON_NAME";    //Matching by token is bad.
					bool isCleanse = affectCleanse && pi.displayNameToken == "SHRINE_CLEANSE_NAME";    //Matching by token is bad.
					bool isPrinter = false;
					if (!isCauldron && !isCleanse)
                    {
						EntityStateMachine esm = self.GetComponent<EntityStateMachine>();
						if (esm && esm.state.GetType() == typeof(EntityStates.Duplicator.Duplicating)) isPrinter = true;
                    }

					if (isCauldron || isCleanse || isPrinter)
					{
						CharacterBody cb = pi.lastActivator.GetComponent<CharacterBody>();
						if (cb && cb.inventory)
						{
							PickupDef pd = PickupCatalog.GetPickupDef(self.pickupIndex);
							if (pd != null && pd.itemIndex != ItemIndex.None)
							{
								cb.inventory.GiveItem(pd.itemIndex, 1);
								addedToInventory = true;
								self.SetHasBeenPurchased(true);
							}
						}
					}
				}
			}
			if (!addedToInventory) orig(self);
        }
    }
}
