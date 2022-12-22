using BepInEx;
using EntityStates;
using EntityStates.Scrapper;
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
	[BepInPlugin("com.Moffein.PrintToInventory", "PrintToInventory", "1.0.0")]

    public class PrintToInventory : BaseUnityPlugin
    {
		//private static SceneDef bazaarDef = Addressables.LoadAssetAsync<SceneDef>("RoR2/Base/bazaar/bazaar.asset").WaitForCompletion();
		public static bool affectScrappers = true;
		public static bool affectPrinters = true;

		public void Awake()
        {
            if (affectScrappers) On.EntityStates.Scrapper.ScrappingToIdle.OnEnter += ScrappingToIdle_OnEnter;
			if (affectPrinters) On.EntityStates.Duplicator.Duplicating.DropDroplet += Duplicating_DropDroplet;
        }

        private void Duplicating_DropDroplet(On.EntityStates.Duplicator.Duplicating.orig_DropDroplet orig, EntityStates.Duplicator.Duplicating self)
        {
			if (self.hasDroppedDroplet)
			{
				return;
			}
			self.hasDroppedDroplet = true;
			if (NetworkServer.active)
			{
				bool addedToInventory = false;
				ShopTerminalBehavior stb = self.GetComponent<ShopTerminalBehavior>();
				PurchaseInteraction pi = self.GetComponent<PurchaseInteraction>();
				if (pi && pi.lastActivator)
                {
					CharacterBody cb = pi.lastActivator.GetComponent<CharacterBody>();
					if (cb && cb.inventory)
					{
						PickupDef pd = PickupCatalog.GetPickupDef(stb.pickupIndex);
						if (pd != null && pd.itemIndex != ItemIndex.None)
						{
							cb.inventory.GiveItem(pd.itemIndex, 1);
							addedToInventory = true;
						}
                    }
                }
				if (!addedToInventory) stb.DropPickup();
			}
			if (self.muzzleTransform)
			{
				if (self.bakeEffectInstance)
				{
					EntityState.Destroy(self.bakeEffectInstance);
				}
				if (EntityStates.Duplicator.Duplicating.releaseEffectPrefab)
				{
					EffectManager.SimpleMuzzleFlash(EntityStates.Duplicator.Duplicating.releaseEffectPrefab, self.gameObject, EntityStates.Duplicator.Duplicating.muzzleString, false);
				}
			}
		}

        private static void ScrappingToIdle_OnEnter(On.EntityStates.Scrapper.ScrappingToIdle.orig_OnEnter orig, ScrappingToIdle self)
        {
            //Not strictly needed here, but just in case a mod does something wacky.
            if (self.characterBody)
            {
                self.attackSpeedStat = self.characterBody.attackSpeed;
                self.damageStat = self.characterBody.damage;
                self.critStat = self.characterBody.crit;
                self.moveSpeedStat = self.characterBody.moveSpeed;
            }

            self.pickupPickerController = self.GetComponent<PickupPickerController>();
            self.scrapperController = self.GetComponent<ScrapperController>();
            self.pickupPickerController.SetAvailable(self.enableInteraction);

			Util.PlaySound(ScrappingToIdle.enterSoundString, self.gameObject);
			self.PlayAnimation("self", "ScrappingToIdle", "Scrapping.playbackRate", ScrappingToIdle.duration);
			if (ScrappingToIdle.muzzleflashEffectPrefab)
			{
				EffectManager.SimpleMuzzleFlash(ScrappingToIdle.muzzleflashEffectPrefab, self.gameObject, ScrappingToIdle.muzzleString, false);
			}
			if (!NetworkServer.active)
			{
				return;
			}
			self.foundValidScrap = false;
			PickupIndex pickupIndex = PickupIndex.none;
			ItemDef itemDef = ItemCatalog.GetItemDef(self.scrapperController.lastScrappedItemIndex);
			if (itemDef != null)
			{
				switch (itemDef.tier)
				{
					case ItemTier.Tier1:
						pickupIndex = PickupCatalog.FindPickupIndex("ItemIndex.ScrapWhite");
						break;
					case ItemTier.Tier2:
						pickupIndex = PickupCatalog.FindPickupIndex("ItemIndex.ScrapGreen");
						break;
					case ItemTier.Tier3:
						pickupIndex = PickupCatalog.FindPickupIndex("ItemIndex.ScrapRed");
						break;
					case ItemTier.Boss:
						pickupIndex = PickupCatalog.FindPickupIndex("ItemIndex.ScrapYellow");
						break;
				}
			}
			if (pickupIndex != PickupIndex.none)
			{
				self.foundValidScrap = true;
				Transform transform = self.FindModelChild(ScrappingToIdle.muzzleString);

				bool addedToInventory = false;
				if (self.scrapperController && self.scrapperController.interactor)
                {
					CharacterBody cb = self.scrapperController.interactor.GetComponent<CharacterBody>();
					if (cb && cb.inventory)
                    {
						PickupDef pd = PickupCatalog.GetPickupDef(pickupIndex);
						if (pd != null && pd.itemIndex != ItemIndex.None)
                        {
							cb.inventory.GiveItem(pd.itemIndex, 1);
							addedToInventory = true;
						}
                    }
                }
				
				if (!addedToInventory) PickupDropletController.CreatePickupDroplet(pickupIndex, transform.position, Vector3.up * ScrappingToIdle.dropUpVelocityStrength + transform.forward * ScrappingToIdle.dropForwardVelocityStrength);

				ScrapperController scrapperController = self.scrapperController;
				int itemsEaten = scrapperController.itemsEaten;
				scrapperController.itemsEaten = itemsEaten - 1;
			}
		}
    }
}
