﻿using Accounting.Service;
using FluentValidation;
using FluentValidation.Results;

namespace Accounting.Models.SecretViewModels
{
  public class CreateSecretViewModel
  {
    private string? _key;
    private string? _value;
    private string? _vendor;
    private string? _purpose;

    public string? Key { get => _key; set => _key = value?.Trim(); }
    public string? Value { get => _value; set => _value = value?.Trim(); }
    public string? Vendor { get => _vendor; set => _vendor = value?.Trim(); }
    public string? Purpose { get => _purpose; set => _purpose = value?.Trim(); }

    public bool EncryptValue { get; set; }
    public bool Master { get; set; }
    public ValidationResult? ValidationResult { get; set; }
    public int OrganizationId { get; set; }
  }

  public class CreateSecretViewModelValidator : AbstractValidator<CreateSecretViewModel>
  {
    private readonly SecretService _secretService;

    public CreateSecretViewModelValidator(SecretService secretService)
    {
      _secretService = secretService;

      RuleFor(x => x.Key).NotEmpty().WithMessage("Key is required.")
                         .MaximumLength(100).WithMessage("Key cannot exceed 100 characters.");
      RuleFor(x => x.Value).NotEmpty().WithMessage("Value is required.");
      RuleFor(x => x.Vendor).MaximumLength(20).WithMessage("Vendor name cannot exceed 20 characters.");
      RuleFor(x => x.Purpose).MaximumLength(100).WithMessage("Purpose cannot exceed 100 characters.");

      RuleFor(x => x)
        .MustAsync(KeyNotExists)
        .WithMessage("The key already exists.");

      RuleFor(x => x)
        .Must(x => !(x.Master && x.EncryptValue))
        .WithMessage("A master key cannot have its value encrypted.");
    }

    private async Task<bool> KeyNotExists(CreateSecretViewModel model, CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(model.Key))
        return true;

      var secret = await _secretService.GetAsync(model.Key, model.OrganizationId);
      return secret == null;
    }
  }
}