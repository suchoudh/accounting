﻿using Accounting.Business;
using Accounting.Common;
using Accounting.CustomAttributes;
using Accounting.Models.ItemViewModels;
using Accounting.Service;
using Accounting.Validators;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using System.Transactions;
using static Accounting.Business.Account;

namespace Accounting.Controllers
{
  [AuthorizeWithOrganizationId]
  [Route("item")]
  public class ItemController : BaseController
  {
    private readonly AccountService _accountService;
    private readonly LocationService _locationService;
    private readonly InventoryService _inventoryService;
    private readonly ItemService _itemService;
    private readonly InventoryAdjustmentService _inventoryAdjustmentService;
    private readonly GeneralLedgerInventoryAdjustmentService _generalLedgerInventoryAdjustmentService;
    private readonly GeneralLedgerService _generalLedgerService;

    public ItemController(
      AccountService accountService, 
      LocationService locationService, 
      InventoryService inventoryService, 
      ItemService itemService,
      InventoryAdjustmentService inventoryAdjustmentService,
      GeneralLedgerInventoryAdjustmentService generalLedgerInventoryAdjustmentService,
      GeneralLedgerService generalLedgerService)
    {
      _accountService = accountService;
      _locationService = locationService;
      _inventoryService = inventoryService;
      _itemService = itemService;
      _inventoryAdjustmentService = inventoryAdjustmentService;
      _generalLedgerInventoryAdjustmentService = generalLedgerInventoryAdjustmentService;
      _generalLedgerService = generalLedgerService;
    }

    [HttpGet]
    [Route("products-and-services")]
    public async Task<IActionResult> ProductsAndServices(int page = 1, int pageSize = 10)
    {
      var vm = new ProductsAndServicesPaginatedViewModel
      {
        Page = page,
        PageSize = pageSize
      };

      return View(vm);
    }
    
    [HttpGet]
    [Route("create/{parentItemId?}")]
    public async Task<IActionResult> Create(int? parentItemId)
    {
      CreateItemViewModel model = new CreateItemViewModel();

      if (parentItemId != null)
      {
        Item parentItem = await _itemService.GetAsync(parentItemId.Value, GetOrganizationId());
        if (parentItem == null)
          return NotFound();

        model.ParentItemId = parentItemId;
        model.ParentItem = new CreateItemViewModel.ItemViewModel
        {
          ItemID = parentItem.ItemID,
          Name = parentItem.Name
        };
      }

      List<Account> accounts
        = await _accountService.GetAllAsync(AccountTypeConstants.Revenue, GetOrganizationId());
      accounts.AddRange(
        await _accountService.GetAllAsync(AccountTypeConstants.Assets, GetOrganizationId()));

      model.Accounts = new List<CreateItemViewModel.AccountViewModel>();
      model.AvailableInventoryMethods = Item.InventoryMethods.All.ToList();
      model.AvailableItemTypes = Item.ItemTypes.All.ToList();
      model.Locations = new List<CreateItemViewModel.LocationViewModel>();

      List<Location> locations = await _locationService.GetAllAsync(GetOrganizationId());

      foreach (var location in locations)
      {
        model.Locations.Add(new CreateItemViewModel.LocationViewModel
        {
          LocationID = location.LocationID,
          Name = location.Name
        });
      }

      foreach (Account account in accounts)
      {
        model.Accounts.Add(new CreateItemViewModel.AccountViewModel
        {
          AccountID = account.AccountID,
          Name = account.Name,
          Type = account.Type
        });
      }

      return View(model);
    }

    [HttpPost]
    [Route("create/{parentItemId?}")]
    public async Task<IActionResult> Create(CreateItemViewModel model)
    {
      CreateItemViewModelValidator validator = new CreateItemViewModelValidator(GetOrganizationId());
      ValidationResult validationResult = await validator.ValidateAsync(model);

      if (!validationResult.IsValid)
      {
        model.ValidationResult = validationResult;

        if (model.ParentItemId != null)
        {
          Item parentItem = await _itemService.GetAsync(model.ParentItemId.Value, GetOrganizationId());
          if (parentItem == null)
            return NotFound();

          model.ParentItemId = parentItem.ItemID;
          model.ParentItem = new CreateItemViewModel.ItemViewModel
          {
            ItemID = parentItem.ItemID,
            Name = parentItem.Name
          };
        }

        List<Account> accounts
        = await _accountService.GetAllAsync(AccountTypeConstants.Revenue, GetOrganizationId());
        accounts.AddRange(
          await _accountService.GetAllAsync(AccountTypeConstants.Assets, GetOrganizationId()));

        model.Accounts = new List<CreateItemViewModel.AccountViewModel>();
        model.AvailableInventoryMethods = Item.InventoryMethods.All.ToList();
        model.AvailableItemTypes = Item.ItemTypes.All.ToList();
        model.Locations = new List<CreateItemViewModel.LocationViewModel>();

        List<Location> locations = await _locationService.GetAllAsync(GetOrganizationId());

        foreach (var location in locations)
        {
          model.Locations.Add(new CreateItemViewModel.LocationViewModel
          {
            LocationID = location.LocationID,
            Name = location.Name
          });
        }

        foreach (Account account in accounts)
        {
          model.Accounts.Add(new CreateItemViewModel.AccountViewModel
          {
            AccountID = account.AccountID,
            Name = account.Name,
            Type = account.Type
          });
        }

        model.SelectedItemType = model.SelectedItemType;

        return View(model);
      }

      Item item = new Item()
      {
        Name = model.Name,
        Description = model.Description,
        RevenueAccountId = model.SelectedRevenueAccountId,
        AssetsAccountId = model.SelectedAssetsAccountId,
        ItemType = model.SelectedItemType,
        InventoryMethod = model.SelectedInventoryMethod!,
        ParentItemId = model.ParentItemId,
        CreatedById = GetUserId(),
        OrganizationId = GetOrganizationId()
      };

      using (TransactionScope scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
      {
        item = await _itemService.CreateAsync(item);

        if (model.SelectedLocationId > 0)
        {
          await _inventoryService.CreateAsync(new Inventory
          {
            ItemId = item.ItemID,
            LocationId = model.SelectedLocationId.Value,
            Quantity = model.Quantity,
            SalePrice = model.SalePrice,
            CreatedById = GetUserId(),
            OrganizationId = GetOrganizationId()
          });

          await _inventoryAdjustmentService.CreateAsync(new InventoryAdjustment
          {
            ItemId = item.ItemID,
            ToLocationId = model.SelectedLocationId.Value,
            Quantity = model.Quantity,
            CreatedById = GetUserId(),
            OrganizationId = GetOrganizationId()
          });

          if (model.InitialCost > 0)
          {
            Guid transactonGuid = GuidExtensions.CreateSecureGuid();

            Account debitInventoryAccount = await _accountService.GetByAccountNameAsync("inventory", GetOrganizationId());
            Account creditInventoryOpeningBalanceAccount = await _accountService.GetByAccountNameAsync("inventory-opening-balance", GetOrganizationId());

            GeneralLedger debitEntry = await _generalLedgerService.CreateAsync(new GeneralLedger
            {
              AccountId = debitInventoryAccount.AccountID,
              Debit = model.InitialCost,
              Credit = null,
              CreatedById = GetUserId(),
              OrganizationId = GetOrganizationId(),
            });

            GeneralLedger creditEntry = await _generalLedgerService.CreateAsync(new GeneralLedger
            {
              AccountId = creditInventoryOpeningBalanceAccount.AccountID,
              Debit = null,
              Credit = model.InitialCost,
              CreatedById = GetUserId(),
              OrganizationId = GetOrganizationId(),
            });

            //await _generalLedgerInventoryAdjustmentService.CreateAsync(new GeneralLedgerInventoryAdjustment
            //{
            //  InventoryAdjustmentId = debitEntry.GeneralLedgerID,
            //  GeneralLedgerId = creditEntry.GeneralLedgerID,
            //  TransactionGuid = transactonGuid,
            //  CreatedById = GetUserId(),
            //  OrganizationId = GetOrganizationId()
            //});
          }
        }

        scope.Complete();
      }

      return RedirectToAction("ProductsAndServices");
    }
  }
}