using System;

namespace Coravel.Scheduling.Schedule.Zoned
{
    internal class ZonedTime
    {
        private readonly TimeZoneInfo _info;

        public ZonedTime(TimeZoneInfo info)
        {
            _info = info;
        }

        // ReSharper disable once InconsistentNaming
        public static ZonedTime AsUTC()
        {
            return new ZonedTime(TimeZoneInfo.Utc);
        }

        public DateTime Convert(DateTime time)
        {
            return _info.Equals(TimeZoneInfo.Utc) ? time : TimeZoneInfo.ConvertTime(time, _info);
        }
    }
}