﻿using System.Threading;

namespace Thermostat.PnPConvention
{
    public enum StatusCodes // for property updates
    {
        Completed = 200,
        Pending = 202,
        Invalid = 400,
        NotImplemented = 404
    }
}
