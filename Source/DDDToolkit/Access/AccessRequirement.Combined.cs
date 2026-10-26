using System.Text;

namespace DDDToolkit.Access;

public abstract partial record AccessRequirement
{
    /// <summary>
    /// Requirements that combine other cases: <see cref="AllOf"/> and <see cref="AnyOf"/>. Decided by the set of checks
    /// itself, <see cref="AccessChecks{TRequests}"/>, which asks each case of the check that decides it.
    /// </summary>
    /// <remarks>
    /// Two combinations are equal when they are of one kind and hold equal cases in the same order, so a test pins a
    /// combined requirement as it pins any other. Written for a log, it names its cases in their order:
    /// <c>AnyOf { Cases = [Operator { }, ForTheWholeTenant { Key = tenancy.seats.manage }] }</c>.
    /// </remarks>
    public abstract record Combined : AccessRequirement
    {
        private readonly AccessRequirement[] _cases;

        /// <summary>The cases, checked and copied, so a caller's array can change afterwards without the requirement changing with it.</summary>
        private protected Combined(AccessRequirement[] cases, string spelling)
        {
            ArgumentNullException.ThrowIfNull(cases);

            if (cases.Length == 0)
            {
                throw new ArgumentException(
                    $"AccessRequirement.{spelling}() takes at least one requirement: a combination of none would require nothing, and there is no requirement that says nothing.",
                    nameof(cases));
            }

            foreach (var each in cases)
            {
                if (each is null)
                {
                    throw new ArgumentException($"AccessRequirement.{spelling}() holds no null: every case says what it requires.", nameof(cases));
                }

                if (each is Anyone)
                {
                    throw new ArgumentException(
                        $"AccessRequirement.{spelling}() does not take AllowAnonymous(): beside another case it opens the door that case closes, or says nothing. A request anyone may send says AllowAnonymous() alone.",
                        nameof(cases));
                }
            }

            _cases = [.. cases];
        }

        /// <summary>The cases, in the order they are asked.</summary>
        public IReadOnlyList<AccessRequirement> Cases => _cases;

        /// <inheritdoc />
        public virtual bool Equals(Combined? other)
            => other is not null && base.Equals(other) && _cases.AsSpan().SequenceEqual(other._cases);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(base.GetHashCode());
            foreach (var each in _cases)
            {
                hash.Add(each);
            }

            return hash.ToHashCode();
        }

        /// <summary>The cases, in their order, as a log line names them.</summary>
        protected override bool PrintMembers(StringBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.Append("Cases = [").AppendJoin(", ", _cases.Select(static each => each.ToString())).Append(']');
            return true;
        }
    }

    /// <summary>
    /// What <see cref="All"/> answers: every case holds, asked in their order, and a refusal is the first failing case's
    /// own.
    /// </summary>
    public sealed record AllOf : Combined
    {
        /// <summary>Made by <see cref="All"/>, the one spelling a request writes.</summary>
        internal AllOf(AccessRequirement[] cases)
            : base(cases, nameof(All))
        {
        }
    }

    /// <summary>
    /// What <see cref="Any"/> answers: the first case the caller meets lets it through, and when none does the refusal
    /// is <see cref="Exceptions.ToolkitRefusals.Refused"/>, with each case's reason in order.
    /// </summary>
    public sealed record AnyOf : Combined
    {
        /// <summary>Made by <see cref="Any"/>, the one spelling a request writes.</summary>
        internal AnyOf(AccessRequirement[] cases)
            : base(cases, nameof(Any))
        {
        }
    }
}
