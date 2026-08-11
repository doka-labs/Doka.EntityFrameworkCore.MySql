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
        "mariadb:10.11.18@sha256:de61fed4a40d3842f3ee09944ba52792156cfd9adf489b2cc670fc6ded28df8d";

    public const string MariaDb114 =
        "mariadb:11.4.12@sha256:a794d9eb009e20de605858a11f32f63b4075cbd197c650436f0e3b457e4caed7";

    public const string MariaDb118 =
        "mariadb:11.8.8@sha256:efb4959ef2c835cd735dbc388eb9ad6aab0c78dd64febcd51bc17481111890c4";

    public const string MariaDb123 =
        "mariadb:12.3.2@sha256:759869cb6f003234a95c6384cdee245b4bce7de26913fe607a8110362c0c007d";
}
