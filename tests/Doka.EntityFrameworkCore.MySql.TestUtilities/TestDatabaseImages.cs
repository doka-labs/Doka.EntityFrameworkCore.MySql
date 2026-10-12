namespace Doka.EntityFrameworkCore.MySql.TestUtilities;

/// <summary>
/// Pins the multi-platform image manifests used by the supported test matrix.
/// </summary>
public static class TestDatabaseImages
{
    public const string MySql84 =
        "mysql:8.4.11@sha256:6ea90827b1100f8f2ae306a539f86d2c264a26ed435a2a9f75551dd5c3aeb242";

    public const string MySql97 =
        "mysql:9.7.2@sha256:e2bde46db6563855d7177adb5f0b57b9dc663f5a20927a90f4259d3312068497";

    public const string MariaDb1011 =
        "mariadb:10.11.19@sha256:7db29378d4fdab73f8123bbc2b48905c90d1a4b00cf848b028f1e81e623257f2";

    public const string MariaDb114 =
        "mariadb:11.4.12@sha256:4f1d8d202fcf7bcb3902f63af09f9c1a050c2922a89652f22abaec0d4f015e83";

    public const string MariaDb118 =
        "mariadb:11.8.8@sha256:24e76fcec8c003a0362d0dd53f4806e7e79458d7fdeaf47437760e19496f5a9c";

    public const string MariaDb123 =
        "mariadb:12.3.2@sha256:a02fe89cb597d4375812b2eac90cf9d0775d4686daa7f7cc750ebbcad7525bbc";
}
