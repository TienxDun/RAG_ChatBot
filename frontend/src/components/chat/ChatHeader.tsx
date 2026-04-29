import * as React from "react";
import { List } from "@phosphor-icons/react";
import { cn } from "@/lib/utils";

interface ChatHeaderProps {
  onOpenSidebar: () => void;
  apiMode: string | undefined;
}

export const ChatHeader: React.FC<ChatHeaderProps> = ({ onOpenSidebar, apiMode }) => {
  return (
    <header className="fixed top-6 left-0 right-0 z-50 px-8 flex justify-between items-center pointer-events-none animate-in fade-in duration-1000">
      <div className="flex items-center gap-4 pointer-events-auto">
        <button 
          onClick={onOpenSidebar}
          className="w-11 h-11 flex items-center justify-center rounded-2xl glass-panel border border-primary/10 text-foreground/60 hover:text-primary hover:border-primary/30 transition-all shadow-sm active:scale-90"
        >
          <List size={24} weight="bold" />
        </button>
        <div className="flex items-center gap-3">
          <h1 className="text-base font-black tracking-[0.1em] text-foreground uppercase">
            SQL AI <span className="text-primary/60">Insight</span>
          </h1>
          <div className="flex items-center gap-2">
            <div className="h-3 w-[1px] bg-foreground/10 hidden sm:block" />
            <div className={cn(
              "px-2 py-0.5 rounded-full text-[9px] font-bold uppercase tracking-wider border",
              apiMode === 'dotnet' 
                ? "bg-blue-500/10 text-blue-500 border-blue-500/20" 
                : "bg-amber-500/10 text-amber-500 border-amber-500/20"
            )}>
              {apiMode === 'dotnet' ? ".NET API" : "Direct Mode"}
            </div>
          </div>
          <p className="text-[9px] font-bold text-muted-foreground/30 uppercase tracking-[0.3em] hidden sm:block">Powered by Gemini</p>
        </div>
      </div>

      <div className="flex items-center gap-2 pointer-events-auto">
        <div className="flex items-center p-1 rounded-full glass-panel border border-primary/5 shadow-sm">
          <button 
            onClick={() => window.location.reload()}
            className="px-4 py-1.5 rounded-full text-[10px] font-black text-muted-foreground/60 hover:text-primary transition-all active:scale-95 uppercase tracking-[0.15em]"
          >
            New Chat
          </button>
        </div>
      </div>
    </header>
  );
};
