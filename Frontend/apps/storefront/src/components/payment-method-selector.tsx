"use client";

import { CreditCard, LoaderCircle, RefreshCw } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { problemMessage } from "@/lib/http/problem-details";

export type PaymentProviderDescriptor = {
  name: string;
  isSandbox: boolean;
  requiresRedirect: boolean;
  supportedCurrencies: string[];
};

type PaymentMethodSelectorProps = {
  currency: string;
  disabled?: boolean;
  onChange: (providerName: string | null) => void;
  selectedProvider: string | null;
};

type LoadState = "loading" | "ready" | "unavailable";

export function PaymentMethodSelector({ currency, disabled = false, onChange, selectedProvider }: PaymentMethodSelectorProps) {
  const [state, setState] = useState<LoadState>("loading");
  const [providers, setProviders] = useState<PaymentProviderDescriptor[]>([]);
  const [message, setMessage] = useState<string | null>(null);

  const load = useCallback(async () => {
    setState("loading");
    setMessage(null);
    try {
      const response = await fetch("/api/payments/providers", { cache: "no-store" });
      const payload: unknown = await response.json().catch(() => null);
      if (!response.ok || !isProviderList(payload)) {
        throw new Error(problemMessage(payload) ?? "Payment methods could not be loaded.");
      }

      setProviders(payload);
      setState("ready");
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Payment methods could not be loaded.");
      setState("unavailable");
      onChange(null);
    }
  }, [onChange]);

  useEffect(() => {
    const task = window.setTimeout(() => { void load(); }, 0);
    return () => window.clearTimeout(task);
  }, [load]);

  const supportedProviders = useMemo(
    () => providers.filter((provider) => provider.supportedCurrencies.some((value) => value === "*" || value.toUpperCase() === currency.toUpperCase())),
    [currency, providers],
  );

  useEffect(() => {
    if (state !== "ready") return;

    if (supportedProviders.length === 0) {
      onChange(null);
      return;
    }

    if (!selectedProvider || !supportedProviders.some((provider) => provider.name === selectedProvider)) {
      onChange(supportedProviders[0].name);
    }
  }, [onChange, selectedProvider, state, supportedProviders]);

  if (state === "loading") {
    return <p className="mt-4 inline-flex items-center gap-2 text-sm text-[var(--muted)]"><LoaderCircle aria-hidden="true" className="animate-spin" size={16} />Loading payment methods...</p>;
  }

  if (state === "unavailable") {
    return <div className="mt-4 border-l-2 border-[var(--danger)] bg-[#fff7f6] px-3 py-3 text-sm" role="alert"><p className="text-[var(--danger)]">{message}</p><button className="mt-3 inline-flex h-9 items-center gap-2 border border-[var(--danger)] px-3 font-semibold text-[var(--danger)] hover:bg-white" onClick={() => void load()} type="button"><RefreshCw aria-hidden="true" size={16} />Retry</button></div>;
  }

  if (supportedProviders.length === 0) {
    return <p className="mt-4 border-l-2 border-[#d8d6c5] bg-[#fbfaf2] px-3 py-3 text-sm text-[var(--muted)]">No configured payment method supports {currency}.</p>;
  }

  return <fieldset className="mt-4" disabled={disabled}><legend className="text-sm font-semibold">Choose payment method</legend><div className="mt-3 divide-y divide-[var(--line)] border-y border-[var(--line)]">{supportedProviders.map((provider) => <label className="flex cursor-pointer items-center gap-3 px-1 py-3 has-[:disabled]:cursor-not-allowed has-[:disabled]:opacity-60" key={provider.name}><input checked={selectedProvider === provider.name} className="size-4 accent-[var(--accent)]" name="payment-provider" onChange={() => onChange(provider.name)} type="radio" value={provider.name} /><CreditCard aria-hidden="true" className="shrink-0 text-[var(--accent)]" size={18} /><span className="min-w-0 flex-1"><span className="block text-sm font-medium">{provider.name}{provider.isSandbox ? " (test)" : ""}</span><span className="mt-0.5 block text-xs text-[var(--muted)]">{provider.requiresRedirect ? "You will continue on the provider's secure page." : "Provider confirmation is required before your order is paid."}</span></span></label>)}</div></fieldset>;
}

function isProviderList(value: unknown): value is PaymentProviderDescriptor[] {
  return Array.isArray(value) && value.every((provider) => {
    if (!provider || typeof provider !== "object") return false;
    const item = provider as Record<string, unknown>;
    return typeof item.name === "string"
      && typeof item.isSandbox === "boolean"
      && typeof item.requiresRedirect === "boolean"
      && Array.isArray(item.supportedCurrencies)
      && item.supportedCurrencies.every((currency) => typeof currency === "string");
  });
}