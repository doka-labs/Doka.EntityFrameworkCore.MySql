namespace Doka.EntityFrameworkCore.MySql.TestUtilities;

/// <summary>
/// Pins the multi-platform image manifests used by the supported test matrix.
/// </summary>
public static class TestDatabaseImages
{
    public const string MySql84 =
        "mysql:8.4.13@sha256:a81d043930e51745522bd5c430961cf8286f0a23f0a9a2a6e0108f7ea71b0902";

    public const string MySql97 =
        "mysql:9.7.4@sha256:df90258da46f6e7ff8574ff84f17618aba0765cac749928299c69fd4e7cb8ad8";

    public const string MariaDb1011 =
        "mariadb:10.11.19@sha256:7d829180b5b2a9ba74f6242876ab401a0d7dd338a807e522ef92bc9698760f20";

    public const string MariaDb114 =
        "mariadb:11.4.13@sha256:5e73baf8179b29f115101bb102763a47a017361f3e9792daccba6646c3ab1532";

    public const string MariaDb118 =
        "mariadb:11.8.9@sha256:d36f18d65a0459510b8b0f643b226a6d299e3206486e2b64ff20754a6d291707";

    public const string MariaDb123 =
        "mariadb:12.3.3@sha256:4ebbcf100a3ba320766dfa0e1af008df12261301db6ef72798c7650f70794aed";
}
