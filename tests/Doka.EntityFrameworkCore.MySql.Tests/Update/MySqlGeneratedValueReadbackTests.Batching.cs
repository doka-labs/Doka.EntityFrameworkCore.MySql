using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlGeneratedValueReadbackTests
{
    /// <summary>
    /// Skips physical readback metadata for write-only commands while preserving unparameterized read columns.
    /// </summary>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public void OnlyCommandsWithReadColumnsInspectReadbackMetadata(
        bool hasReadResults,
        int expectedTableAccesses
    )
    {
        using var context = new PacketContext(CreatePacketOptions());
        var command = new TestCommand([], hasReadResults);
        var batch = context.GetService<IModificationCommandBatchFactory>().Create();

        var added = batch.TryAddCommand(command);

        Assert.True(added);
        Assert.Equal(expectedTableAccesses, command.ReadbackTableAccesses);
        Assert.Single(batch.ModificationCommands);
    }

    /// <summary>
    /// Charges repeated binary key literals to the command packet before reaching the 1,000-command limit.
    /// </summary>
    [Fact]
    public void RepeatedKeyReadbackBytesCloseTheBatchBeforeThePacketBudget()
    {
        using var context = new PacketContext(CreatePacketOptions());
        var commands = CreatePacketCommands(context, count: 1000);
        var batch = context.GetService<IModificationCommandBatchFactory>().Create();

        var accepted = AddUntilFull(batch, commands);
        var repeatedRejection = accepted < commands.Length && batch.TryAddCommand(commands[accepted]);

        Assert.InRange(accepted, 1, 999);
        Assert.Equal(accepted, batch.ModificationCommands.Count);
        Assert.False(repeatedRejection);
        Assert.Equal(accepted, batch.ModificationCommands.Count);
    }

    /// <summary>
    /// Does not commit projected packet costs when rejecting a command, so a smaller valid key can still fit.
    /// </summary>
    [Fact]
    public void RejectedPacketProjectionDoesNotConsumeTheRemainingBudget()
    {
        using var context = new PacketContext(CreatePacketOptions());
        var commands = CreatePacketCommands(context, count: 1000);
        var smaller = CreatePacketCommand(context, [255]);
        var batch = context.GetService<IModificationCommandBatchFactory>().Create();
        var accepted = AddUntilFull(batch, commands);

        var added = batch.TryAddCommand(smaller);

        Assert.InRange(accepted, 1, 999);
        Assert.True(added);
        Assert.Equal(accepted + 1, batch.ModificationCommands.Count);
    }

    /// <summary>
    /// Keeps the explicit no-transaction path on its existing per-row fallback,
    /// without charging unused readback SQL.
    /// </summary>
    [Fact]
    public void TransactionOptOutDoesNotChargeInactiveKeyedReadback()
    {
        using var context = new PacketContext(CreatePacketOptions());
        context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
        var commands = CreatePacketCommands(context, count: 1000);
        var batch = context.GetService<IModificationCommandBatchFactory>().Create();

        var accepted = AddUntilFull(batch, commands);

        Assert.Equal(1000, accepted);
        Assert.Equal(1000, batch.ModificationCommands.Count);
    }

    private static int AddUntilFull(
        ModificationCommandBatch batch,
        IModificationCommand[] commands
    )
    {
        var accepted = 0;
        while (accepted < commands.Length && batch.TryAddCommand(commands[accepted]))
        {
            accepted++;
        }

        return accepted;
    }

    private static IModificationCommand[] CreatePacketCommands(
        PacketContext context,
        int count
    )
    {
        var commands = new IModificationCommand[count];
        for (var index = 0; index < count; index++)
        {
            var key = new byte[3071];
            BinaryPrimitives.WriteInt32LittleEndian(key, index + 1);
            commands[index] = CreatePacketCommand(context, key);
        }

        return commands;
    }

    private static IModificationCommand CreatePacketCommand(
        PacketContext context,
        byte[] key
    )
    {
        var entity = new PacketRow { Id = key };
        context.Add(entity);
        var primaryKey = context.Model.FindEntityType(typeof(PacketRow))!.FindPrimaryKey()!;
        var entry = context.GetService<IUpdateAdapterFactory>().Create().TryGetEntry(primaryKey, [key])!;

        var table = context.Model.GetRelationalModel().Tables.Single();
        var command = context.GetService<IModificationCommandFactory>().CreateModificationCommand(
            new ModificationCommandParameters(
                table,
                sensitiveLoggingEnabled: false,
                detailedErrorsEnabled: false,
                comparer: null,
                generateParameterName: context.NextParameterName,
                logger: null));

        command.AddEntry(entry, mainEntry: true);

        return command;
    }

    private static DbContextOptions CreatePacketOptions() => new DbContextOptionsBuilder()
        .UseMySql(
            "Server=localhost;Database=readback_packet_unit;User ID=test;Password=test;",
            MySqlServerVersion.MySql(new Version(8, 4, 11)),
            mysql => mysql.MaxBatchSize(1000))
        .Options;

    /// <summary>
    /// Gives the packet tests native-valid binary keys at the physical InnoDB key-width boundary.
    /// </summary>
    private sealed class PacketContext : DbContext
    {
        private int _parameterIndex;

        public PacketContext(
            DbContextOptions options
        ) : base(options) { }

        public string NextParameterName() => $"p{_parameterIndex++}";

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<PacketRow>(row =>
            {
                row.HasKey(candidate => candidate.Id);
                row.Property(candidate => candidate.Id).HasMaxLength(3071).ValueGeneratedNever();
                row.Property(candidate => candidate.Generated).HasComputedColumnSql("1", stored: true);
            });
        }
    }

    private sealed class PacketRow
    {
        public byte[] Id { get; set; } = [];

        public int Generated { get; set; }
    }
}
