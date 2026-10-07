using Keypaste.Mvvm;
using Xunit;

namespace Keypaste.App.Tests.Mvvm;

/// <summary>
/// A view model declares what each computed property and command reads, and a single
/// <c>Set</c> raises the rest.
/// </summary>
public sealed class ObservableObjectTests
{
    [Fact]
    public void A_change_raises_the_property_then_each_dependent_in_declaration_order()
    {
        var model = new Sample();
        model.DependsOn(nameof(Sample.Sum), nameof(Sample.A), nameof(Sample.B));
        model.DependsOn(nameof(Sample.Doubled), nameof(Sample.A));
        var raised = Record(model);

        model.A = 1;

        Assert.Equal([nameof(Sample.A), nameof(Sample.Sum), nameof(Sample.Doubled)], raised);
    }

    [Fact]
    public void An_unchanged_value_raises_nothing_and_asks_no_command()
    {
        var model = new Sample();
        var command = new RelayCommand(() => { });
        model.DependsOn(nameof(Sample.Sum), nameof(Sample.A));
        model.DependsOn(command, nameof(Sample.A));
        var raised = Record(model);
        var asked = 0;
        command.CanExecuteChanged += (_, _) => asked++;

        Assert.False(model.SetA(0));

        Assert.Empty(raised);
        Assert.Equal(0, asked);
    }

    [Fact]
    public void Undeclared_properties_raise_only_themselves()
    {
        var model = new Sample();
        var raised = Record(model);

        model.A = 1;
        model.B = 2;

        Assert.Equal([nameof(Sample.A), nameof(Sample.B)], raised);
    }

    [Fact]
    public void A_dependent_of_a_dependent_is_raised()
    {
        var model = new Sample();
        model.DependsOn(nameof(Sample.Sum), nameof(Sample.A));
        model.DependsOn(nameof(Sample.Doubled), nameof(Sample.Sum));
        var raised = Record(model);

        model.A = 1;

        Assert.Equal([nameof(Sample.A), nameof(Sample.Sum), nameof(Sample.Doubled)], raised);
    }

    [Fact]
    public void A_dependent_reached_two_ways_is_raised_once()
    {
        var model = new Sample();
        model.DependsOn(nameof(Sample.Sum), nameof(Sample.A));
        model.DependsOn(nameof(Sample.B), nameof(Sample.A));
        model.DependsOn(nameof(Sample.Doubled), nameof(Sample.Sum), nameof(Sample.B));
        var raised = Record(model);

        model.A = 1;

        Assert.Equal([nameof(Sample.A), nameof(Sample.Sum), nameof(Sample.B), nameof(Sample.Doubled)], raised);
    }

    [Fact]
    public void Properties_declared_on_each_other_raise_each_once_rather_than_forever()
    {
        var model = new Sample();
        model.DependsOn(nameof(Sample.B), nameof(Sample.A));
        model.DependsOn(nameof(Sample.A), nameof(Sample.B));
        var raised = Record(model);

        model.A = 1;

        Assert.Equal([nameof(Sample.A), nameof(Sample.B)], raised);
    }

    [Fact]
    public void Raising_a_computed_property_raises_what_depends_on_it()
    {
        var model = new Sample();
        model.DependsOn(nameof(Sample.Doubled), nameof(Sample.Sum));
        var raised = Record(model);

        model.RaiseSum();

        Assert.Equal([nameof(Sample.Sum), nameof(Sample.Doubled)], raised);
    }

    [Fact]
    public void A_command_is_asked_again_when_what_it_reads_changes_and_not_otherwise()
    {
        var model = new Sample();
        var command = new RelayCommand(() => { }, () => model.A > 0);
        model.DependsOn(command, nameof(Sample.A));
        var asked = 0;
        command.CanExecuteChanged += (_, _) => asked++;

        model.B = 1;
        Assert.Equal(0, asked);
        Assert.False(command.CanExecute(null));

        model.A = 1;
        Assert.Equal(1, asked);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public void A_command_reached_through_several_dependents_is_asked_once()
    {
        var model = new Sample();
        var command = new AsyncRelayCommand(() => Task.CompletedTask);
        model.DependsOn(nameof(Sample.Sum), nameof(Sample.A));
        model.DependsOn(nameof(Sample.Doubled), nameof(Sample.A));
        model.DependsOn(command, nameof(Sample.A), nameof(Sample.Sum), nameof(Sample.Doubled));
        var asked = 0;
        command.CanExecuteChanged += (_, _) => asked++;

        model.A = 1;

        Assert.Equal(1, asked);
    }

    [Fact]
    public void A_declaration_made_after_a_change_is_honoured_by_the_next()
    {
        var model = new Sample();
        model.DependsOn(nameof(Sample.Sum), nameof(Sample.A));
        model.A = 1;
        model.DependsOn(nameof(Sample.Doubled), nameof(Sample.A));
        var raised = Record(model);

        model.A = 2;

        Assert.Equal([nameof(Sample.A), nameof(Sample.Sum), nameof(Sample.Doubled)], raised);
    }

    private static List<string> Record(Sample model)
    {
        var raised = new List<string>();
        model.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);
        return raised;
    }

    private sealed class Sample : ObservableObject
    {
        private int _a;
        private int _b;

        public int A
        {
            get => _a;
            set => Set(ref _a, value);
        }

        public int B
        {
            get => _b;
            set => Set(ref _b, value);
        }

        public int Sum => _a + _b;

        public int Doubled => Sum * 2;

        public bool SetA(int value) => Set(ref _a, value, nameof(A));

        public void RaiseSum() => Raise(nameof(Sum));

        public new void DependsOn(string dependent, params ReadOnlySpan<string> sources) =>
            base.DependsOn(dependent, sources);

        public new void DependsOn(IRelayCommand command, params ReadOnlySpan<string> sources) =>
            base.DependsOn(command, sources);
    }
}
