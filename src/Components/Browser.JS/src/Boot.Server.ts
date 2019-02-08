import '@dotnet/jsinterop';
import './GlobalExports';
import * as signalR from '@aspnet/signalr';
import { MessagePackHubProtocol } from '@aspnet/signalr-protocol-msgpack';
import { OutOfProcessRenderBatch } from './Rendering/RenderBatch/OutOfProcessRenderBatch';
import { internalFunctions as uriHelperFunctions } from './Services/UriHelper';
import { renderBatch } from './Rendering/Renderer';
import { fetchBootConfigAsync, loadEmbeddedResourcesAsync } from './BootCommon';
import { CircuitHandler } from './Platform/Circuits/CircuitHandler';

async function boot(reconnect: boolean = false) {
  const circuitHandlers = new Array<CircuitHandler>();
  window['Blazor'].addCircuitHandler = (circuitHandler: CircuitHandler) => circuitHandlers.push(circuitHandler);

  await startCicuit();

  async function startCicuit(): Promise<void> {
    // In the background, start loading the boot config and any embedded resources
    const embeddedResourcesPromise = fetchBootConfigAsync().then(bootConfig => {
      return loadEmbeddedResourcesAsync(bootConfig);
    });

    const initialConnection = await initializeConnection();

    // Ensure any embedded resources have been loaded before starting the app
    await embeddedResourcesPromise;
    const circuitId = await initialConnection.invoke<string>(
      'StartCircuit',
      uriHelperFunctions.getLocationHref(),
      uriHelperFunctions.getBaseURI()
    );

    window['Blazor'].reconnect = async () => {
      const reconnection = await initializeConnection();
      if (!await reconnection.invoke<Boolean>('ConnectCircuit', circuitId)) {
        throw "Failed to reconnect to the server";
      }

      circuitHandlers.forEach(h => h.onConnectionUp());
    };

    circuitHandlers.forEach(h => h.onConnectionUp());
  }

  async function initializeConnection(): Promise<signalR.HubConnection> {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('_blazor')
      .withHubProtocol(new MessagePackHubProtocol())
      .configureLogging(signalR.LogLevel.Information)
      .build();
    connection.on('JS.BeginInvokeJS', DotNet.jsCallDispatcher.beginInvokeJSFromDotNet);
    connection.on('JS.RenderBatch', (browserRendererId: number, renderId: number, batchData: Uint8Array) => {
      try {
        renderBatch(browserRendererId, new OutOfProcessRenderBatch(batchData));
        connection.send('OnRenderCompleted', renderId, null);
      }
      catch (ex) {
        // If there's a rendering exception, notify server *and* throw on client
        connection.send('OnRenderCompleted', renderId, ex.toString());
        throw ex;
      }
    });

    connection.onclose(error => circuitHandlers.forEach(h => h.onConnectionDown(error)));
    connection.on('JS.Error', unhandledError.bind(connection));

    window['Blazor'].closeConnection = async() => {
      await connection.stop();
      DotNet.attachDispatcher({
        beginInvokeDotNetFromJS: (...args) => {}});
    }

    try {
      await connection.start();
    } catch (ex) {
      unhandledError.call(connection, ex);
    }

    DotNet.attachDispatcher({
      beginInvokeDotNetFromJS: (callId, assemblyName, methodIdentifier, dotNetObjectId, argsJson) => {
        connection.send('BeginInvokeDotNetFromJS', callId ? callId.toString() : null, assemblyName, methodIdentifier, dotNetObjectId || 0, argsJson);
      }
    });

    return connection;
  }

  function unhandledError(this: signalR.HubConnection, err) {
    console.error(err);

    // Disconnect on errors.
    //
    // TODO: it would be nice to have some kind of experience for what happens when you're
    // trying to interact with an app that's disconnected.
    //
    // Trying to call methods on the connection after its been closed will throw.
    if (this) {
      this.stop();
    }
  }
}

boot();
