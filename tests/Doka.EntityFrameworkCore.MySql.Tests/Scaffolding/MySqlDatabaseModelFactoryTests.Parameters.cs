namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlDatabaseModelFactoryTests
{
    private sealed class ScaffoldingDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }

        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;

        public override bool IsNullable { get; set; }

        [AllowNull]
        public override string ParameterName { get; set; } = string.Empty;

        [AllowNull]
        public override string SourceColumn { get; set; } = string.Empty;

        public override object? Value { get; set; }

        public override bool SourceColumnNullMapping { get; set; }

        public override int Size { get; set; }

        public override void ResetDbType() { }
    }

    private sealed class ScaffoldingDbParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _parameters = [];

        public override int Count => _parameters.Count;

        public override object SyncRoot => ((ICollection)_parameters).SyncRoot;

        public override int Add(
            object value
        )
        {
            ArgumentNullException.ThrowIfNull(value);
            _parameters.Add((DbParameter)value);

            return _parameters.Count - 1;
        }

        public override void AddRange(
            Array values
        )
        {
            ArgumentNullException.ThrowIfNull(values);

            foreach (var value in values)
            {
                Add(value!);
            }
        }

        public override void Clear() => _parameters.Clear();

        public override bool Contains(
            object value
        )
        {
            ArgumentNullException.ThrowIfNull(value);

            return _parameters.Contains((DbParameter)value);
        }

        public override bool Contains(
            string value
        ) => _parameters.Any(parameter => parameter.ParameterName == value);

        public override void CopyTo(
            Array array,
            int index
        ) => ((ICollection)_parameters).CopyTo(array, index);

        public override IEnumerator GetEnumerator() => _parameters.GetEnumerator();

        public override int IndexOf(
            object value
        )
        {
            ArgumentNullException.ThrowIfNull(value);

            return _parameters.IndexOf((DbParameter)value);
        }

        public override int IndexOf(
            string parameterName
        ) => _parameters.FindIndex(parameter => parameter.ParameterName == parameterName);

        public override void Insert(
            int index,
            object value
        )
        {
            ArgumentNullException.ThrowIfNull(value);
            _parameters.Insert(index, (DbParameter)value);
        }

        public override void Remove(
            object value
        )
        {
            ArgumentNullException.ThrowIfNull(value);
            _parameters.Remove((DbParameter)value);
        }

        public override void RemoveAt(
            int index
        ) => _parameters.RemoveAt(index);

        public override void RemoveAt(
            string parameterName
        )
        {
            var index = IndexOf(parameterName);

            if (index >= 0)
            {
                RemoveAt(index);
            }
        }

        protected override DbParameter GetParameter(
            int index
        ) => _parameters[index];

        protected override DbParameter GetParameter(
            string parameterName
        )
        {
            var index = IndexOf(parameterName);

            return index >= 0
                ? _parameters[index]
                : throw new ArgumentException($"Parameter '{parameterName}' was not found.", nameof(parameterName));
        }

        protected override void SetParameter(
            int index,
            DbParameter value
        )
        {
            ArgumentNullException.ThrowIfNull(value);
            _parameters[index] = value;
        }

        protected override void SetParameter(
            string parameterName,
            DbParameter value
        )
        {
            ArgumentNullException.ThrowIfNull(value);

            var index = IndexOf(parameterName);

            if (index >= 0)
            {
                _parameters[index] = value;

                return;
            }

            _parameters.Add(value);
        }
    }
}
