import type { Config } from 'tailwindcss';

export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        solid: {
          ink: '#20242A',
          blue: '#2F80ED',
          surface: '#F8FAFC'
        }
      }
    }
  },
  plugins: []
} satisfies Config;
