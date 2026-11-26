namespace Explore.Domain.Learning;

public enum LearningPracticeAction
{
    Start = 1,
    Choose = 2,
    Confirm = 3,
    Reset = 4,
    Clear = 5
}

/// <summary>Authored recipe option slots, interpreted only by the Application catalogue.</summary>
public enum LearningPracticeChoice
{
    None = 0,
    Primary = 1,
    Secondary = 2,
    Tertiary = 3
}
