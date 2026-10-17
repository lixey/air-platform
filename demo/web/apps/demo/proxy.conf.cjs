module.exports = {
  '/api/**': {
    target: process.env.DEMO_API_URL || 'http://localhost:5091',
    secure: false,
    changeOrigin: true,
  },
};
