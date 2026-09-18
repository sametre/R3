using FluentValidation;
using R3.Contracts;

namespace R3.Application;

public sealed record CreateProductRequest(string Code, string Name, Guid BaseUnitId, decimal VatRate);
public sealed class CreateProductValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductValidator() { RuleFor(x => x.Code).NotEmpty().WithMessage("Ürün kodu zorunludur."); RuleFor(x => x.Name).NotEmpty().WithMessage("Ürün adı zorunludur."); RuleFor(x => x.BaseUnitId).NotEmpty().WithMessage("Temel birim zorunludur."); RuleFor(x => x.VatRate).InclusiveBetween(0, 100).WithMessage("KDV oranı 0 ile 100 arasında olmalıdır."); }
}
public sealed record CreateAccountRequest(string Code, string Name);
public sealed class CreateAccountValidator : AbstractValidator<CreateAccountRequest>
{
    public CreateAccountValidator() { RuleFor(x => x.Code).NotEmpty().WithMessage("Cari kodu zorunludur."); RuleFor(x => x.Name).NotEmpty().WithMessage("Cari adı zorunludur."); }
}
public sealed record CreateWarehouseCommand(Guid CompanyId, Guid BranchId, string Code, string Name);
public sealed class CreateWarehouseValidator : AbstractValidator<CreateWarehouseCommand>
{
    public CreateWarehouseValidator() { RuleFor(x => x.CompanyId).NotEmpty().WithMessage("Firma zorunludur."); RuleFor(x => x.BranchId).NotEmpty().WithMessage("Şube zorunludur."); RuleFor(x => x.Code).NotEmpty().WithMessage("Depo kodu zorunludur."); RuleFor(x => x.Name).NotEmpty().WithMessage("Depo adı zorunludur."); }
}
public sealed class CreateCompanyValidator : AbstractValidator<CreateCompanyRequest>
{
    public CreateCompanyValidator() { RuleFor(x => x.Code).NotEmpty().MaximumLength(40).WithMessage("Firma kodu zorunludur ve 40 karakteri geçemez."); RuleFor(x => x.Name).NotEmpty().MaximumLength(200).WithMessage("Firma adı zorunludur."); RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).WithMessage("E-posta adresi geçerli değil."); RuleFor(x => x.TaxNumber).Must(CanonicalRules.BeValidTaxNumber).When(x => !string.IsNullOrWhiteSpace(x.TaxNumber)).WithMessage("Vergi numarası 10 (VKN) veya 11 (TCKN) rakamdan oluşmalıdır."); }
}
public sealed class UpdateCompanyValidator : AbstractValidator<UpdateCompanyRequest>
{
    public UpdateCompanyValidator() { RuleFor(x => x.Code).NotEmpty().MaximumLength(40).WithMessage("Firma kodu zorunludur."); RuleFor(x => x.Name).NotEmpty().MaximumLength(200).WithMessage("Firma adı zorunludur."); RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).WithMessage("E-posta adresi geçerli değil."); RuleFor(x => x.TaxNumber).Must(CanonicalRules.BeValidTaxNumber).When(x => !string.IsNullOrWhiteSpace(x.TaxNumber)).WithMessage("Vergi numarası 10 (VKN) veya 11 (TCKN) rakamdan oluşmalıdır."); }
}
internal static class CanonicalRules
{
    public static bool BeValidTaxNumber(string value)
    {
        var normalized = value.Trim();
        return normalized.Length is 10 or 11 && normalized.All(char.IsDigit);
    }
}
public sealed class CreateBranchContractValidator : AbstractValidator<CreateBranchRequest>
{
    public CreateBranchContractValidator() { RuleFor(x => x.CompanyId).NotEmpty().WithMessage("Firma zorunludur."); RuleFor(x => x.Code).NotEmpty().MaximumLength(40).WithMessage("Şube kodu zorunludur."); RuleFor(x => x.Name).NotEmpty().MaximumLength(200).WithMessage("Şube adı zorunludur."); }
}
public sealed class UpdateBranchContractValidator : AbstractValidator<UpdateBranchRequest>
{
    public UpdateBranchContractValidator() { RuleFor(x => x.CompanyId).NotEmpty(); RuleFor(x => x.Code).NotEmpty().MaximumLength(40); RuleFor(x => x.Name).NotEmpty().MaximumLength(200); }
}
public sealed class CreateWarehouseContractValidator : AbstractValidator<CreateWarehouseRequest>
{
    public CreateWarehouseContractValidator() { RuleFor(x => x.CompanyId).NotEmpty().WithMessage("Firma zorunludur."); RuleFor(x => x.BranchId).NotEmpty().WithMessage("Şube zorunludur."); RuleFor(x => x.Code).NotEmpty().MaximumLength(40).WithMessage("Depo kodu zorunludur."); RuleFor(x => x.Name).NotEmpty().MaximumLength(200).WithMessage("Depo adı zorunludur."); }
}
public sealed class UpdateWarehouseContractValidator : AbstractValidator<UpdateWarehouseRequest>
{
    public UpdateWarehouseContractValidator() { RuleFor(x => x.CompanyId).NotEmpty(); RuleFor(x => x.BranchId).NotEmpty(); RuleFor(x => x.Code).NotEmpty().MaximumLength(40); RuleFor(x => x.Name).NotEmpty().MaximumLength(200); }
}
