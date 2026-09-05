namespace Doka.EntityFrameworkCore.MySql.TestUtilities;

/// <summary>
/// Pins the multi-platform image manifests used by the supported test matrix.
/// </summary>
public static class TestDatabaseImages
{
    public const string MySql84 =
        "mysql:8.4.11@sha256:b3b90af2a6552ae30c266fdb7d5dd55f3afb72404bb78d37fe8a23eb857fd3fb";

    public const string MySql97 =
        "mysql:9.7.2@sha256:257388edf9c84dbc04c763625446d5f3fa6ed60d1b0873bc552c614ba0a7ab4e";

    public const string MariaDb1011 =
        "mariadb:10.11.19@sha256:ce66c7be32a03aabe7241d0a10993a2db827ef652a35d25727d92a832ac8ef73";

    public const string MariaDb114 =
        "mariadb:11.4.12@sha256:4f1d8d202fcf7bcb3902f63af09f9c1a050c2922a89652f22abaec0d4f015e83";

    public const string MariaDb118 =
        "mariadb:11.8.8@sha256:24e76fcec8c003a0362d0dd53f4806e7e79458d7fdeaf47437760e19496f5a9c";

    public const string MariaDb123 =
        "mariadb:12.3.2@sha256:a02fe89cb597d4375812b2eac90cf9d0775d4686daa7f7cc750ebbcad7525bbc";
}
