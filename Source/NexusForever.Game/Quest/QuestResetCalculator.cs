using NexusForever.Game.Static.Quest;

namespace NexusForever.Game.Quest
{
    /// <summary>
    /// Calculates strictly-future UTC reset boundaries for repeatable quests.
    /// </summary>
    internal static class QuestResetCalculator
    {
        private const int ResetHourUtc = 10;

        /// <summary>
        /// Returns whether the supplied repeat period has a supported reset policy.
        /// </summary>
        public static bool IsSupported(QuestRepeatPeriod period)
        {
            return period is QuestRepeatPeriod.Daily
                or QuestRepeatPeriod.Weekly
                or QuestRepeatPeriod.Monthly
                or QuestRepeatPeriod.Yearly;
        }

        /// <summary>
        /// Attempts to calculate the first reset boundary strictly after the supplied completion time.
        /// </summary>
        public static bool TryCalculateNext(
            QuestRepeatPeriod period,
            DateTime completionTime,
            out DateTime resetTime)
        {
            DateTime completionUtc = AsUtc(completionTime);
            resetTime = default;

            switch (period)
            {
                case QuestRepeatPeriod.Daily:
                {
                    resetTime = completionUtc.Date.AddHours(ResetHourUtc);
                    if (resetTime <= completionUtc)
                        resetTime = resetTime.AddDays(1d);
                    return true;
                }
                case QuestRepeatPeriod.Weekly:
                {
                    int daysUntilTuesday = ((int)DayOfWeek.Tuesday - (int)completionUtc.DayOfWeek + 7) % 7;
                    resetTime = completionUtc.Date.AddDays(daysUntilTuesday).AddHours(ResetHourUtc);
                    if (resetTime <= completionUtc)
                        resetTime = resetTime.AddDays(7d);
                    return true;
                }
                case QuestRepeatPeriod.Monthly:
                {
                    resetTime = new DateTime(
                        completionUtc.Year,
                        completionUtc.Month,
                        1,
                        ResetHourUtc,
                        0,
                        0,
                        DateTimeKind.Utc);
                    if (resetTime <= completionUtc)
                        resetTime = resetTime.AddMonths(1);
                    return true;
                }
                case QuestRepeatPeriod.Yearly:
                {
                    resetTime = new DateTime(
                        completionUtc.Year,
                        1,
                        1,
                        ResetHourUtc,
                        0,
                        0,
                        DateTimeKind.Utc);
                    if (resetTime <= completionUtc)
                        resetTime = resetTime.AddYears(1);
                    return true;
                }
                default:
                    return false;
            }
        }

        /// <summary>
        /// Normalises a timestamp to UTC, treating database timestamps without a kind as UTC values.
        /// </summary>
        public static DateTime AsUtc(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc         => value,
                DateTimeKind.Local       => value.ToUniversalTime(),
                DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
                _                        => value
            };
        }
    }
}
