import clsx from 'clsx';
import type { ButtonHTMLAttributes, InputHTMLAttributes, PropsWithChildren } from 'react';

export function Card({ children, className }: PropsWithChildren<{ className?: string }>) {
  return <section className={clsx('rounded-2xl border border-slate-200 bg-white p-5 shadow-sm', className)}>{children}</section>;
}

export function Button({ children, className, ...props }: PropsWithChildren<ButtonHTMLAttributes<HTMLButtonElement>>) {
  return <button className={clsx('rounded-xl bg-solid-blue px-4 py-2 font-semibold text-white shadow-sm disabled:opacity-50', className)} {...props}>{children}</button>;
}

export function Input(props: InputHTMLAttributes<HTMLInputElement>) {
  return <input className="w-full rounded-xl border border-slate-300 px-3 py-2 outline-none focus:border-solid-blue" {...props} />;
}

export function Label({ children }: PropsWithChildren) {
  return <label className="text-sm font-medium text-slate-700">{children}</label>;
}

export function Badge({ children, tone = 'neutral' }: PropsWithChildren<{ tone?: 'neutral' | 'good' | 'warn' }>) {
  const styles = {
    neutral: 'bg-slate-100 text-slate-700',
    good: 'bg-emerald-100 text-emerald-800',
    warn: 'bg-amber-100 text-amber-800'
  };
  return <span className={clsx('rounded-full px-3 py-1 text-xs font-semibold', styles[tone])}>{children}</span>;
}
