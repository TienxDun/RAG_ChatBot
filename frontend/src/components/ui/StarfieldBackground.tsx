"use client";

import React, { useEffect, useRef } from "react";

interface Star {
  x: number;
  y: number;
  size: number;
  opacity: number;
  opacitySpeed: number;
  velocity: number;
  phase: number;
}

interface StarfieldBackgroundProps {
  starCount?: number;
  speed?: number;
  interactive?: boolean;
}

const StarfieldBackground: React.FC<StarfieldBackgroundProps> = ({
  starCount = 150,
  speed = 0.05,
  interactive = true,
}) => {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const starsRef = useRef<Star[]>([]);
  const mouseRef = useRef({ x: 0, y: 0 });

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    const initStars = () => {
      const { width, height } = canvas;
      const stars: Star[] = [];
      for (let i = 0; i < starCount; i++) {
        stars.push({
          x: Math.random() * width,
          y: Math.random() * height,
          size: Math.random() * 1.5 + 0.5,
          opacity: Math.random() * 0.5 + 0.5,
          opacitySpeed: Math.random() * 0.05 + 0.01,
          velocity: Math.random() * speed + 0.02,
          phase: Math.random() * Math.PI * 2,
        });
      }
      starsRef.current = stars;
    };

    const handleResize = () => {
      canvas.width = window.innerWidth;
      canvas.height = window.innerHeight;
      initStars();
    };

    const handleMouseMove = (e: MouseEvent) => {
      mouseRef.current = {
        x: (e.clientX - window.innerWidth / 2) * 0.05,
        y: (e.clientY - window.innerHeight / 2) * 0.05,
      };
    };

    window.addEventListener("resize", handleResize);
    if (interactive) {
      window.addEventListener("mousemove", handleMouseMove);
    }

    handleResize();

    let animationFrameId: number;

    const render = () => {
      ctx.clearRect(0, 0, canvas.width, canvas.height);
      
      // Draw background (optional, can let layout handle it)
      // ctx.fillStyle = "#0F172A"; 
      // ctx.fillRect(0, 0, canvas.width, canvas.height);

      starsRef.current.forEach((star) => {
        // Update phase for sine-based twinkling
        star.phase += star.opacitySpeed;
        
        // Smoother twinkling using sine wave
        const currentOpacity = (Math.sin(star.phase) + 1) / 2; // Normalize to 0-1
        const opacityRange = star.opacity * currentOpacity;

        // Parallax movement
        const targetX = star.x - mouseRef.current.x * star.velocity * 5;
        const targetY = star.y - mouseRef.current.y * star.velocity * 5;

        // Draw star
        ctx.beginPath();
        ctx.arc(targetX, targetY, star.size, 0, Math.PI * 2);
        
        // Detect if we are in dark mode to adjust star color
        const isDark = document.documentElement.classList.contains("dark");
        ctx.fillStyle = isDark 
          ? `rgba(255, 255, 255, ${opacityRange})` 
          : `rgba(124, 58, 237, ${opacityRange * 0.5})`; // Purple stars in light mode
        ctx.fill();

        // Subtle glow for larger stars
        if (star.size > 1.2) {
          ctx.shadowBlur = 4 * currentOpacity;
          ctx.shadowColor = isDark ? "white" : "rgba(124, 58, 237, 0.5)";
        } else {
          ctx.shadowBlur = 0;
        }
      });

      animationFrameId = requestAnimationFrame(render);
    };

    render();

    return () => {
      window.removeEventListener("resize", handleResize);
      window.removeEventListener("mousemove", handleMouseMove);
      cancelAnimationFrame(animationFrameId);
    };
  }, [starCount, speed, interactive]);

  return (
    <canvas
      ref={canvasRef}
      className="fixed inset-0 z-0 pointer-events-none transition-opacity duration-1000"
      style={{ background: "transparent" }}
    />
  );
};

export default StarfieldBackground;
