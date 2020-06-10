﻿
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Text;
using System.Threading.Tasks;

namespace PnPConvention
{
  public abstract class PnPComponent
  {
    public DeviceClient client;

    public readonly string componentName;
    private readonly ILogger logger;

    public delegate void OnDesiredPropertyFoundCallback(object newValue);

    public PnPComponent(DeviceClient client, string componentname)
        : this(client, componentname, new NullLogger<PnPComponent>()) { }

    public PnPComponent(DeviceClient client, string componentname, ILogger log)
    {
      this.componentName = componentname;
      this.client = client;
      this.logger = log;
      this.logger.LogInformation("New PnPComponent for " + componentname);
    }

    public async Task SendTelemetryValueAsync(string serializedTelemetry)
    {
      this.logger.LogTrace($"Sending Telemetry [${serializedTelemetry}]");
      var message = new Message(Encoding.UTF8.GetBytes(serializedTelemetry));
      message.Properties.Add("$.sub", this.componentName);
      message.ContentType = "application/json";
      message.ContentEncoding = "utf-8";
      await this.client.SendEventAsync(message);
    }

    public async Task ReportProperty(string propertyName, object propertyValue)
    {
      logger.LogTrace("Reporting " + propertyName);
      var twin = new TwinCollection();
      twin.AddComponentProperty(this.componentName, propertyName, propertyValue);
      await this.client.UpdateReportedPropertiesAsync(twin);
    }
    public async Task SetPnPCommandHandler(string commandName, MethodCallback callback, object ctx)
    {
      this.logger.LogTrace("Set Command Handler for " + commandName);
      await this.client.SetMethodHandlerAsync($"{this.componentName}*{commandName}", callback, ctx);
    }

    public async Task<string> ReadDesiredProperty(string propertyName)
    {
      this.logger.LogTrace("ReadDesiredProperty " + propertyName);
      var twin = await this.client.GetTwinAsync();
      var result = twin.Properties.Desired.GetPropertyValue<string>(this.componentName, propertyName);
      this.logger.LogTrace("ReadDesiredProperty returned: " + result);
      return result;
    }

    public void SetPnPDesiredPropertyHandler(string propertyName, OnDesiredPropertyFoundCallback callback, object ctx)
    {
      StatusCodes result = StatusCodes.NotImplemented;
      this.logger.LogTrace("Set Desired Handler for " + propertyName);
      this.client.SetDesiredPropertyUpdateCallbackAsync(async (TwinCollection desiredProperties, object ctx2) =>
      {

        this.logger.LogTrace($"Received desired updates [{desiredProperties.ToJson()}]");
        string desiredPropertyValue = desiredProperties.GetPropertyValue<string>(this.componentName, propertyName);
        result = StatusCodes.Pending;
        await AckDesiredPropertyReadAsync(propertyName, desiredPropertyValue, StatusCodes.Pending, "update in progress", desiredProperties.Version);

        if (!string.IsNullOrEmpty(desiredPropertyValue))
        {
          callback(desiredPropertyValue);
          result = StatusCodes.Completed;
          await AckDesiredPropertyReadAsync(propertyName, desiredPropertyValue, StatusCodes.Completed, "update complete", desiredProperties.Version);
          this.logger.LogTrace($"Desired properties processed successfully");
        }
        else
        {
          result = StatusCodes.Invalid;
          await AckDesiredPropertyReadAsync(propertyName, desiredPropertyValue, StatusCodes.Invalid, "invalid, empty value", desiredProperties.Version);
          this.logger.LogTrace($"Invalid desired properties processed ");
        }
        await Task.FromResult(result);
      }, this).Wait();
    }

    async Task AckDesiredPropertyReadAsync(string propertyName, object payload, StatusCodes statuscode, string description, long version)
    {
      var ack = new TwinCollection();
      SetAck(ack, propertyName, payload, statuscode, version, description);
      await client.UpdateReportedPropertiesAsync(ack);
      this.logger.LogTrace($"Reported writable property [{this.componentName}] - {JsonConvert.SerializeObject(payload)}");
    }

    void SetAck(TwinCollection ack, string propertyName, object value, StatusCodes statusCode, long statusVersion, string statusDescription = "")
    {
      var property = new TwinCollection();
      property["value"] = value;
      property["ac"] = statusCode;
      property["av"] = statusVersion;
      if (!string.IsNullOrEmpty(statusDescription)) property["ad"] = statusDescription;

      if (ack.Contains(this.componentName))
      {
        JToken token = JToken.FromObject(property);
        ack[this.componentName][propertyName] = token;
      }
      else
      {
        TwinCollection root = new TwinCollection();
        root["__t"] = "c"; // TODO: Review, should the ACK require the flag
        root[propertyName] = property;
        ack[this.componentName] = root;
      }
    }
  }
}
