import * as React from "react";
import { Copy, Check, TerminalWindow } from "@phosphor-icons/react";
import { cn } from "@/lib/utils";

interface TerminalCodeBlockProps {
  language?: string;
  value: string;
}

export const TerminalCodeBlock: React.FC<TerminalCodeBlockProps> = ({ language, value }) => {
  const [hasCopied, setHasCopied] = React.useState(false);

  const onCopy = React.useCallback(() => {
    navigator.clipboard.writeText(value);
    setHasCopied(true);
    setTimeout(() => setHasCopied(false), 2000);
  }, [value]);

  return (
    <div className="group relative my-6 overflow-hidden rounded-xl border border-white/10 bg-[#0d1117] shadow-2xl transition-all hover:shadow-primary/5">
      {/* Terminal Header */}
      <div className="flex items-center justify-between bg-white/5 px-4 py-2.5 border-b border-white/5">
        <div className="flex items-center gap-2">
          <div className="flex gap-1.5">
            <div className="h-2.5 w-2.5 rounded-full bg-[#ff5f56]" />
            <div className="h-2.5 w-2.5 rounded-full bg-[#ffbd2e]" />
            <div className="h-2.5 w-2.5 rounded-full bg-[#27c93f]" />
          </div>
          <div className="ml-4 flex items-center gap-1.5 text-[11px] font-medium text-slate-400 uppercase tracking-wider">
            <TerminalWindow size={14} weight="bold" />
            {language || "terminal"}
          </div>
        </div>
        <button
          onClick={onCopy}
          className="flex items-center gap-1.5 rounded-md bg-white/5 px-2.5 py-1 text-[11px] font-medium text-slate-300 transition-all hover:bg-white/10 hover:text-white active:scale-95"
        >
          {hasCopied ? (
            <>
              <Check size={14} weight="bold" className="text-green-400" />
              <span className="text-green-400">Copied</span>
            </>
          ) : (
            <>
              <Copy size={14} />
              <span>Copy</span>
            </>
          )}
        </button>
      </div>

      {/* Code Content */}
      <div className="relative">
        <pre className={cn(
          "scrollbar-thin scrollbar-thumb-white/10 scrollbar-track-transparent overflow-x-auto p-4 text-[13px] md:text-sm leading-relaxed",
          "font-mono text-slate-200"
        )}>
          <code>{value}</code>
        </pre>
      </div>
      
      {/* Subtle Glow Effect */}
      <div className="absolute -inset-px rounded-xl border border-primary/20 opacity-0 transition-opacity group-hover:opacity-100 pointer-events-none" />
    </div>
  );
};
