import { CircuitHandler } from "./CircuitHandler";
export class AutoReconnectCircuitHandler implements CircuitHandler {
  modal: HTMLDivElement;
  intervalHandle: number | null;

  constructor(private maxRetries: number = 3, private retryInterval: number = 3000) {
    const modal = document.createElement('div');
    modal.className = "modal";
    modal.appendChild(document.createTextNode("Attempting to reconnect to the server..."));
    document.addEventListener("DOMContentLoaded", function (event) {
      document.body.appendChild(modal);
    });

    this.modal = modal;
    this.intervalHandle = null;
  }
  onConnectionUp() {
    this.modal.style.display = 'none';
    this.cleanupTimer();
  }
  async onConnectionDown() {
    this.modal.style.display = 'block';
    this.cleanupTimer();

    let retries = 0;
    this.intervalHandle = window.setInterval(async () => {
      if (retries++ > this.maxRetries) {
        this.cleanupTimer();
      }

      try {
        await window['Blazor'].reconnect();
      } catch (err) {
        if (retries < this.maxRetries) {
          console.error(err);
          return;
        }
        throw err;
      }
    }, this.retryInterval);
  }

  cleanupTimer() {
    if (this.intervalHandle) {
      window.clearTimeout(this.intervalHandle);
    }
  }
}
