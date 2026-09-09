"use client";

import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { useEffect, useRef } from "react";

export type CustomerRealtimeUpdate = {
  eventId: string;
  eventType: "order.created" | "order.status-changed";
  resourceId: string;
  status: string;
  occurredAtUtc: string;
  correlationId: string | null;
};

export function useCustomerRealtime(
  onUpdate: (update: CustomerRealtimeUpdate) => void,
  enabled: boolean,
) {
  const callback = useRef(onUpdate);
  const seenEventIds = useRef(new Set<string>());
  useEffect(() => {
    callback.current = onUpdate;
  }, [onUpdate]);


  useEffect(() => {
    if (!enabled) return;

    const connection = new HubConnectionBuilder()
      .withUrl("/realtime/customer-events", { withCredentials: true })
      .withAutomaticReconnect([0, 2_000, 10_000, 30_000])
      .configureLogging(LogLevel.Warning)
      .build();

    const receive = (update: CustomerRealtimeUpdate) => {
      if (!isUpdate(update) || seenEventIds.current.has(update.eventId)) return;

      seenEventIds.current.add(update.eventId);
      if (seenEventIds.current.size > 512) {
        const oldest = seenEventIds.current.values().next().value;
        if (oldest) seenEventIds.current.delete(oldest);
      }

      callback.current(update);
    };

    connection.on("ReceiveCustomerUpdate", receive);
    void connection.start().catch(() => {
      // The REST screen remains usable and reconnect is attempted by SignalR when possible.
    });

    return () => {
      connection.off("ReceiveCustomerUpdate", receive);
      void connection.stop();
    };
  }, [enabled]);
}

function isUpdate(value: unknown): value is CustomerRealtimeUpdate {
  if (!value || typeof value !== "object") return false;
  const update = value as Record<string, unknown>;
  return typeof update.eventId === "string"
    && typeof update.eventType === "string"
    && typeof update.resourceId === "string"
    && typeof update.status === "string"
    && typeof update.occurredAtUtc === "string";
}
