namespace Doka.EntityFrameworkCore.MySql;

internal interface IMySqlProviderOwnedModelTypeMapping
{
    Type ProviderClrType { get; }

    object ConvertToModelValue(object providerValue);
}
