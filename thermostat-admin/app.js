const express = require('express')
const path = require('path')

const bodyParser = require('body-parser')
const hub = require('./app.iothub.js')

const http = require('http')
const WebSocket = require('ws')
const EventHubReader = require('./app.eventHub')

const port = 3000

let connectionString = process.env.IOTHUB_CONNECTION_STRING

if (!connectionString || connectionString.length < 10) {
  console.log('IOTHUB_CONNECTION_STRING not found')
}

const app = express()
const router = express.Router()

app.use(bodyParser.json())
app.use(bodyParser.urlencoded({ extended: true }))
app.use('/api', router)
app.use(express.static('ui'))

const server = http.createServer(app)
const wss = new WebSocket.Server({ server })

wss.broadcast = (data) => {
  wss.clients.forEach((client) => {
    if (client.readyState === WebSocket.OPEN) {
      try {
        client.send(data)
      } catch (e) {
        console.error(e)
      }
    }
  })
}

router.get('/', (req, res, next) => res.sendFile('index.html', { root: path.join(__dirname, 'wwwroot/index.html') }))

router.get('/connection-string', (req, res) => {
  if (connectionString && connectionString.length > 0) {
    const hubRegex = /(?<=HostName=).*(?=;SharedAccessKeyName)/i.exec(connectionString)
    const hubName = hubRegex.length > 0 ? hubRegex[0] : ''
    res.json(hubName)
  } else {
    res.json('not configured')
  }
})

router.post('/connection-string', (req, res) => {
  connectionString = req.body.connectionstring
  if (connectionString && connectionString.length > 0) {
    const hubRegex = /(?<=HostName=).*(?=;SharedAccessKeyName)/i.exec(connectionString)
    const hubName = hubRegex.length > 0 ? hubRegex[0] : ''
    res.json(hubName)
  } else {
    res.json('not configured')
  }
})

router.get('/getDevices', (req, res) => {
  if (connectionString.length > 0) {
    hub.getDeviceList(connectionString, list => res.json(list))
  } else {
    res.json({})
  }
})

router.get('/getDeviceTwin', async (req, res) => {
  const result = await hub.getDeviceTwin(connectionString, req.query.deviceId)
  res.json(result.responseBody)
})

router.get('/getModelId', async (req, res) => {
  const result = await hub.getModelId(connectionString, req.query.deviceId)
  res.json(result)
})

router.post('/updateDeviceTwin', async (req, res) => {
  const result = await hub.updateDeviceTwin(connectionString, req.body.deviceId, req.body.propertyName, req.body.propertyValue)
  console.log('twin updated')
  res.json(result.responseBody)
})

router.post('/invokeCommand', async (req, res) => {
  console.log(`Running command: ${req.body.command}`)
  const result = await hub.invokeDeviceMethod(
    connectionString,
    req.body.deviceId,
    req.body.commandName,
    req.body.payload)

  res.json(result)
})

const eventHubConsumerGroup = process.env.EventHubConsumerGroup
const eventHubReader = new EventHubReader(connectionString, eventHubConsumerGroup)

server.listen(port, () => console.log(`IoT Express app listening on port ${port}`))

;(async () => {
  await eventHubReader.startReadMessage((message, date, deviceId) => {
    try {
      const payload = {
        IotData: message,
        MessageDate: date || Date.now().toISOString(),
        DeviceId: deviceId
      }
      // console.log(message)
      wss.broadcast(JSON.stringify(payload))
    } catch (err) {
      console.error('Error broadcasting: [%s] from [%s].', err, message)
    }
  })
})()
